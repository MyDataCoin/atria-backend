using Atria.Domain.TravelRule;

namespace Atria.Application.Abstractions;

/// <summary>
/// Aggregate repository for <see cref="TravelRuleMessage"/>, the record of originator/beneficiary
/// information owed on transfers between service providers.
/// </summary>
public interface ITravelRuleRepository : IRepository<TravelRuleMessage>
{
    /// <summary>
    /// Messages not yet delivered — the queue. Oldest first: an undelivered obligation only gets
    /// worse with age.
    /// </summary>
    Task<IReadOnlyList<TravelRuleMessage>> ListOutstandingAsync(CancellationToken ct);

    /// <summary>Messages for an issue, newest first.</summary>
    Task<IReadOnlyList<TravelRuleMessage>> ListByPropertyAsync(Guid propertyId, CancellationToken ct);

    /// <summary>
    /// The message already recorded for this transaction and address pair, or null. One transfer owes
    /// one disclosure; a replayed block must not produce a second.
    /// </summary>
    Task<TravelRuleMessage?> FindByTransferAsync(
        string transactionHash, string originatorAddress, string beneficiaryAddress, CancellationToken ct);
}
