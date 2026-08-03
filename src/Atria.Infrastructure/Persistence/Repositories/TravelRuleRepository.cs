using Atria.Application.Abstractions;
using Atria.Domain.TravelRule;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class TravelRuleRepository : Repository<TravelRuleMessage>, ITravelRuleRepository
{
    public TravelRuleRepository(AtriaDbContext db) : base(db) { }

    // Tracked: the delivery worker mutates what it reads.
    public async Task<IReadOnlyList<TravelRuleMessage>> ListOutstandingAsync(CancellationToken ct)
        => await Set
            .Where(m => m.Status == TravelRuleStatus.Pending || m.Status == TravelRuleStatus.Failed)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TravelRuleMessage>> ListByPropertyAsync(
        Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(m => m.PropertyId == propertyId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<TravelRuleMessage?> FindByTransferAsync(
        string transactionHash, string originatorAddress, string beneficiaryAddress, CancellationToken ct)
        => Set.AsNoTracking().FirstOrDefaultAsync(
            m => m.TransactionHash == transactionHash
                 && m.OriginatorAddress == originatorAddress
                 && m.BeneficiaryAddress == beneficiaryAddress,
            ct);
}
