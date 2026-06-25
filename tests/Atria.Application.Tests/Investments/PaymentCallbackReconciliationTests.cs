using Atria.Application.Abstractions;
using Atria.Application.Investments.Commands;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// Regression coverage for payment reconciliation in
/// <see cref="HandlePaymentCallbackCommandHandler"/>: a signed, authentic <c>Completed</c>
/// callback whose <c>Amount</c> does not equal the investment's owed amount must NOT activate
/// the investment. The investment must end <see cref="InvestmentStatus.Failed"/> (amount/currency
/// mismatch is recorded as a failed payment), never <see cref="InvestmentStatus.Active"/>.
///
/// Mirrors the existing idempotency tests' style: NSubstitute for the provider strategy / repos /
/// unit of work, and a real in-memory <see cref="IProcessedEventStore"/> double.
/// </summary>
public sealed class PaymentCallbackReconciliationTests
{
    private const string Provider = "Stripe";
    private const string Currency = "USD";

    private readonly IPaymentProviderStrategy _strategy = Substitute.For<IPaymentProviderStrategy>();
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly InMemoryProcessedEventStore _processed = new();

    private HandlePaymentCallbackCommandHandler CreateSut() =>
        new(new[] { _strategy }, _investments, _processed, _uow);

    private static WebhookPayload Payload() =>
        new(RawBody: "{}", Headers: new Dictionary<string, string>(), Signature: "sig",
            Timestamp: null, SourceIp: null);

    [Fact]
    public async Task Completed_callback_with_mismatched_amount_does_not_activate_investment()
    {
        // Arrange — an investment owing exactly 1000 USD.
        var investment = InvestmentFactory.CreateFromApprovedApplication(
            applicationId: Guid.NewGuid(), investorId: Guid.NewGuid(), propertyId: Guid.NewGuid(),
            amount: 1_000m, currency: Currency);

        _strategy.ProviderType.Returns(PaymentProviderType.Stripe);
        _strategy.VerifySignature(Arg.Any<WebhookPayload>()).Returns(true);
        // Authentic, signed callback — but the paid amount (999) != owed (1000).
        _strategy.ParseCallback(Arg.Any<WebhookPayload>()).Returns(new PaymentCallbackResult(
            ExternalPaymentId: "pi_123",
            InvestmentId: investment.Id,
            Decision: PaymentDecision.Completed,
            Amount: 999m,
            Currency: Currency,
            FailureReason: null,
            EventId: Guid.NewGuid().ToString()));
        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new HandlePaymentCallbackCommand(Provider, Payload()), CancellationToken.None);

        // Assert — handled, but the under-payment was reconciled to Failed, never Active.
        result.IsSuccess.Should().BeTrue();
        investment.Status.Should().Be(InvestmentStatus.Failed);
        investment.Status.Should().NotBe(InvestmentStatus.Active);
        _investments.Received(1).Update(investment);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Completed_callback_with_matching_amount_activates_investment()
    {
        // Arrange — control case: paid amount/currency match exactly, so the investment activates.
        var investment = InvestmentFactory.CreateFromApprovedApplication(
            applicationId: Guid.NewGuid(), investorId: Guid.NewGuid(), propertyId: Guid.NewGuid(),
            amount: 1_000m, currency: Currency);

        _strategy.ProviderType.Returns(PaymentProviderType.Stripe);
        _strategy.VerifySignature(Arg.Any<WebhookPayload>()).Returns(true);
        _strategy.ParseCallback(Arg.Any<WebhookPayload>()).Returns(new PaymentCallbackResult(
            ExternalPaymentId: "pi_456",
            InvestmentId: investment.Id,
            Decision: PaymentDecision.Completed,
            Amount: 1_000m,
            Currency: Currency,
            FailureReason: null,
            EventId: Guid.NewGuid().ToString()));
        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new HandlePaymentCallbackCommand(Provider, Payload()), CancellationToken.None);

        // Assert — a faithful payment activates the investment (proves the test above is meaningful).
        result.IsSuccess.Should().BeTrue();
        investment.Status.Should().Be(InvestmentStatus.Active);
    }

    /// <summary>Real (non-mock) processed-event ledger; mirrors the existing idempotency tests.</summary>
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
