using Atria.Application.Abstractions;
using Atria.Application.Investments.EventHandlers;
using Atria.Domain.Applications.Events;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// Idempotency + happy-path coverage for <see cref="CreateInvestmentOnApplicationApprovedHandler"/>:
/// a redelivered ApplicationApprovedEvent (same EventId) must create the Investment at most once,
/// distinct events each create one, and an already-existing investment is a no-op create.
/// Uses a real in-memory <see cref="IProcessedEventStore"/> double for the exactly-once guard.
/// </summary>
public sealed class CreateInvestmentOnApplicationApprovedHandlerTests
{
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly InMemoryProcessedEventStore _processed = new();

    private CreateInvestmentOnApplicationApprovedHandler CreateSut() =>
        new(_investments, _properties, _processed, _uow);

    private Property ArrangeProperty(Guid propertyId, string currency = "USD")
    {
        var property = Property.Create(
            name: "Tower One",
            description: null,
            address: null,
            totalValue: 1_000_000m,
            tokenPrice: 100m,
            totalTokens: 10_000,
            currency: currency);
        _properties.GetByIdAsync(propertyId, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    [Fact]
    public async Task Same_event_handled_twice_creates_investment_exactly_once()
    {
        // Arrange
        var propertyId = Guid.NewGuid();
        ArrangeProperty(propertyId);
        _investments.GetByApplicationIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Investment?)null);
        var evt = new ApplicationApprovedEvent(
            ApplicationId: Guid.NewGuid(),
            PropertyId: propertyId,
            InvestorId: Guid.NewGuid(),
            Amount: 5_000m);
        var sut = CreateSut();

        // Act — at-least-once redelivery of the SAME event instance.
        await sut.HandleAsync(evt, CancellationToken.None);
        await sut.HandleAsync(evt, CancellationToken.None);

        // Assert — the Investment was persisted once across both invocations.
        await _investments.Received(1).AddAsync(Arg.Any<Investment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_different_events_create_investment_twice()
    {
        // Arrange
        var propertyId = Guid.NewGuid();
        ArrangeProperty(propertyId);
        _investments.GetByApplicationIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Investment?)null);
        var first = new ApplicationApprovedEvent(Guid.NewGuid(), propertyId, Guid.NewGuid(), 5_000m);
        var second = new ApplicationApprovedEvent(Guid.NewGuid(), propertyId, Guid.NewGuid(), 6_000m);
        var sut = CreateSut();

        // Act — two distinct events (distinct EventIds) must both create.
        await sut.HandleAsync(first, CancellationToken.None);
        await sut.HandleAsync(second, CancellationToken.None);

        // Assert
        await _investments.Received(2).AddAsync(Arg.Any<Investment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creates_investment_with_property_currency_when_none_exists()
    {
        // Arrange
        var propertyId = Guid.NewGuid();
        ArrangeProperty(propertyId, currency: "EUR");
        _investments.GetByApplicationIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Investment?)null);
        var evt = new ApplicationApprovedEvent(
            ApplicationId: Guid.NewGuid(),
            PropertyId: propertyId,
            InvestorId: Guid.NewGuid(),
            Amount: 7_500m);
        Investment? added = null;
        await _investments.AddAsync(
            Arg.Do<Investment>(i => added = i), Arg.Any<CancellationToken>());
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(evt, CancellationToken.None);

        // Assert — a PendingPayment investment built from the event, settled in the property's currency.
        await _investments.Received(1).AddAsync(Arg.Any<Investment>(), Arg.Any<CancellationToken>());
        added.Should().NotBeNull();
        added!.ApplicationId.Should().Be(evt.ApplicationId);
        added.InvestorId.Should().Be(evt.InvestorId);
        added.PropertyId.Should().Be(evt.PropertyId);
        added.Amount.Should().Be(7_500m);
        added.Currency.Should().Be("EUR");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_nothing_when_investment_already_exists()
    {
        // Arrange — second guard hits: an investment already exists for the application.
        var propertyId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var existing = InvestmentFactory.CreateFromApprovedApplication(
            applicationId, Guid.NewGuid(), propertyId, 1_000m, "USD");
        _investments.GetByApplicationIdAsync(applicationId, Arg.Any<CancellationToken>())
            .Returns(existing);
        var evt = new ApplicationApprovedEvent(applicationId, propertyId, Guid.NewGuid(), 1_000m);
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(evt, CancellationToken.None);

        // Assert — no new investment created; property never even resolved.
        await _investments.DidNotReceive().AddAsync(Arg.Any<Investment>(), Arg.Any<CancellationToken>());
        await _properties.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Real (non-mock) <see cref="IProcessedEventStore"/> backed by a <see cref="HashSet{T}"/>.
    /// Mirrors the production idempotency ledger so the handler's exactly-once guard is genuinely exercised.
    /// </summary>
    private sealed class InMemoryProcessedEventStore : IProcessedEventStore
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public Task<bool> IsProcessedAsync(string key, CancellationToken ct)
            => Task.FromResult(_keys.Contains(key));

        public Task MarkProcessedAsync(string key, CancellationToken ct)
        {
            _keys.Add(key);
            return Task.CompletedTask;
        }
    }
}
