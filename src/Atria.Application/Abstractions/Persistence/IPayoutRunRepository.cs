using Atria.Domain.Payouts;

namespace Atria.Application.Abstractions;

/// <summary>
/// Aggregate repository for <see cref="PayoutRun"/>, the register of distributions to holders.
/// </summary>
public interface IPayoutRunRepository : IRepository<PayoutRun>
{
    /// <summary>A run with all of its lines loaded, tracked so settlements can be recorded.</summary>
    Task<PayoutRun?> GetWithItemsAsync(Guid id, CancellationToken ct);

    /// <summary>Run headers for an issue, newest cut first. Lines are not loaded.</summary>
    Task<IReadOnlyList<PayoutRun>> ListByPropertyAsync(Guid propertyId, CancellationToken ct);

    /// <summary>
    /// A run already computed against this snapshot, or null. Two runs on one cut would pay the
    /// same register twice, so the creation path checks here first.
    /// </summary>
    Task<PayoutRun?> FindBySnapshotAsync(Guid snapshotId, CancellationToken ct);

    /// <summary>Every distribution an investor is a line of, newest first — their payout history.</summary>
    Task<IReadOnlyList<(PayoutRun Run, PayoutItem Item)>> ListForInvestorAsync(
        Guid investorId, CancellationToken ct);
}
