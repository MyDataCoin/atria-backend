using Atria.Application.Abstractions;
using Atria.Application.Compliance.EventHandlers;
using Atria.Domain.Compliance;
using Atria.Domain.Investments.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atria.Application.Tests.Compliance;

/// <summary>
/// Proves exactly-once for the money/token effects of
/// <see cref="AddToAllowlistOnPaymentCompletedHandler"/>: a redelivery of the SAME event
/// (same EventId) must add to the allowlist and enqueue the token allocation at most once.
/// Uses a real in-memory <see cref="IProcessedEventStore"/> double so the idempotency guard
/// is exercised end-to-end.
/// </summary>
public sealed class AddToAllowlistOnPaymentCompletedHandlerIdempotencyTests
{
    private const string ValidWallet = "0x1234567890abcdef1234567890abcdef12345678";

    private readonly IComplianceRepository _profiles = Substitute.For<IComplianceRepository>();
    private readonly ITesseraComplianceService _tessera = Substitute.For<ITesseraComplianceService>();
    private readonly IBlockchainOperationQueue _queue = Substitute.For<IBlockchainOperationQueue>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly InMemoryProcessedEventStore _processed = new();

    private AddToAllowlistOnPaymentCompletedHandler CreateSut() =>
        new(_profiles, _tessera, _queue, _processed, _uow,
            NullLogger<AddToAllowlistOnPaymentCompletedHandler>.Instance);

    private void ArrangeHappyPath(Guid investorId)
    {
        var profile = ComplianceProfile.Create(investorId, ValidWallet);
        _profiles.GetByInvestorAsync(investorId, Arg.Any<CancellationToken>())
            .Returns(profile);
        _tessera.VerifyPresentationAsync(investorId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    [Fact]
    public async Task Same_event_handled_twice_adds_to_allowlist_exactly_once()
    {
        // Arrange
        var investorId = Guid.NewGuid();
        ArrangeHappyPath(investorId);
        var evt = new PaymentCompletedEvent(
            InvestmentId: Guid.NewGuid(),
            InvestorId: investorId,
            Amount: 1000m,
            ExternalPaymentId: "pay_123");
        var sut = CreateSut();

        // Act — at-least-once redelivery of the SAME event instance.
        await sut.HandleAsync(evt, CancellationToken.None);
        await sut.HandleAsync(evt, CancellationToken.None);

        // Assert — the money/token effects fired once across both invocations.
        await _tessera.Received(1).AddToAllowlistAsync(ValidWallet, Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(
            BlockchainOperationType.TokenAllocation,
            Arg.Any<string>(),
            $"TokenAllocation:{evt.EventId}",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_different_events_add_to_allowlist_twice()
    {
        // Arrange
        var investorId = Guid.NewGuid();
        ArrangeHappyPath(investorId);
        var first = new PaymentCompletedEvent(Guid.NewGuid(), investorId, 1000m, "pay_a");
        var second = new PaymentCompletedEvent(Guid.NewGuid(), investorId, 2000m, "pay_b");
        var sut = CreateSut();

        // Act — two distinct events (distinct EventIds) must both take effect.
        await sut.HandleAsync(first, CancellationToken.None);
        await sut.HandleAsync(second, CancellationToken.None);

        // Assert
        await _tessera.Received(2).AddToAllowlistAsync(ValidWallet, Arg.Any<CancellationToken>());
        await _queue.Received(2).EnqueueAsync(
            BlockchainOperationType.TokenAllocation,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_compliance_profile_skips_allowlist_and_marks_processed()
    {
        // Arrange — happy path NOT arranged; repository returns null profile.
        var investorId = Guid.NewGuid();
        _profiles.GetByInvestorAsync(investorId, Arg.Any<CancellationToken>())
            .Returns((ComplianceProfile?)null);
        var evt = new PaymentCompletedEvent(Guid.NewGuid(), investorId, 500m, "pay_x");
        var sut = CreateSut();

        // Act
        await sut.HandleAsync(evt, CancellationToken.None);

        // Assert — no on-chain effect, but the event is recorded as processed.
        await _tessera.DidNotReceive().AddToAllowlistAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().EnqueueAsync(
            Arg.Any<BlockchainOperationType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _processed.IsProcessedAsync(
            $"{nameof(AddToAllowlistOnPaymentCompletedHandler)}:{evt.EventId}", CancellationToken.None))
            .Should().BeTrue();
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
