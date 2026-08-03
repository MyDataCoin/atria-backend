using Atria.Domain.Refunds;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="RefundObligation"/>, the register of money owed back.</summary>
public interface IRefundObligationRepository : IRepository<RefundObligation>
{
    /// <summary>Obligations for an issue, newest first.</summary>
    Task<IReadOnlyList<RefundObligation>> ListByPropertyAsync(Guid propertyId, CancellationToken ct);

    /// <summary>Everything still owed, oldest first — the settlement queue.</summary>
    Task<IReadOnlyList<RefundObligation>> ListPendingAsync(CancellationToken ct);
}
