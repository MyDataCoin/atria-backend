using Atria.Application.Abstractions;
using Atria.Application.Investments.EventHandlers;
using Atria.Domain.Common;
using Atria.Domain.Investments;
using Atria.Domain.Investments.Events;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// Regression coverage for token allocation / oversubscription in
/// <see cref="AllocateTokensOnInvestmentActivatedHandler"/>:
/// (1) it decrements <see cref="Property.AvailableTokens"/> by the correct unit count for the
///     paid amount, and is idempotent — a redelivery of the SAME event (same EventId) must not
///     decrement supply again; and
/// (2) <see cref="Property.AllocateTokens"/> itself throws when the requested count exceeds the
///     available supply (the oversubscription guard the handler relies on).
///
/// Mirrors the existing handler tests: NSubstitute for the property repo / unit of work, and a
/// real in-memory <see cref="IProcessedEventStore"/> double for the exactly-once guard.
/// </summary>
public sealed class AllocateTokensOnInvestmentActivatedHandlerTests
{
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly InMemoryProcessedEventStore _processed = new();

    private AllocateTokensOnInvestmentActivatedHandler CreateSut() =>
        new(_properties, _processed, _uow);

    private static Property CreateProperty(decimal tokenPrice = 100m, long totalTokens = 10_000) =>
        Property.Create(
            name: "Tower One", description: null, address: null,
            totalValue: 1_000_000m, tokenPrice: tokenPrice, totalTokens: totalTokens, currency: "USD");

    [Fact]
    public async Task Allocates_correct_token_count_and_is_idempotent_on_redelivery()
    {
        // Arrange — 50 tokens bought => 50 whole tokens leave supply.
        var property = CreateProperty(tokenPrice: 100m, totalTokens: 10_000);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        var evt = new InvestmentActivatedEvent(
            InvestmentId: Guid.NewGuid(), InvestorId: Guid.NewGuid(), PropertyId: property.Id,
            TokenCount: 50, Amount: 5_000m);
        var sut = CreateSut();

        // Act — at-least-once redelivery of the SAME event instance.
        await sut.HandleAsync(evt, CancellationToken.None);
        await sut.HandleAsync(evt, CancellationToken.None);

        // Assert — supply decremented exactly once (10000 - 50), not twice.
        property.AvailableTokens.Should().Be(10_000 - 50);
        // The DB write only happened on the first (un-processed) delivery.
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_different_events_each_decrement_supply()
    {
        // Arrange — control case proving the idempotency above is meaningful.
        var property = CreateProperty(tokenPrice: 100m, totalTokens: 10_000);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        var first = new InvestmentActivatedEvent(Guid.NewGuid(), Guid.NewGuid(), property.Id, 50, 5_000m);
        var second = new InvestmentActivatedEvent(Guid.NewGuid(), Guid.NewGuid(), property.Id, 30, 3_000m);
        var sut = CreateSut();

        // Act — two distinct events (distinct EventIds).
        await sut.HandleAsync(first, CancellationToken.None);
        await sut.HandleAsync(second, CancellationToken.None);

        // Assert — 50 + 30 tokens allocated across the two events.
        property.AvailableTokens.Should().Be(10_000 - 50 - 30);
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AllocateTokens_throws_when_count_exceeds_available_supply()
    {
        // Arrange — only 10 tokens in the whole property.
        var property = CreateProperty(tokenPrice: 100m, totalTokens: 10);

        // Act — attempt to oversubscribe by one token.
        var act = () => property.AllocateTokens(11);

        // Assert — the domain guard prevents oversubscription.
        act.Should().Throw<DomainException>()
            .WithMessage("*more tokens than are available*");
        property.AvailableTokens.Should().Be(10, "a rejected allocation must not change supply");
    }

    /// <summary>Real (non-mock) processed-event ledger; mirrors the existing handler tests.</summary>
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
