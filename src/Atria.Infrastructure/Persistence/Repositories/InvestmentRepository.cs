using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class InvestmentRepository : Repository<Investment>, IInvestmentRepository
{
    public InvestmentRepository(AtriaDbContext db) : base(db) { }

    // Read-only list view; callers project scalar fields only.
    public async Task<IReadOnlyList<Investment>> GetByInvestorAsync(Guid investorId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(i => i.InvestorId == investorId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Investment>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return Array.Empty<Investment>();

        return await Set.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(ct);
    }

    public async Task<(decimal TotalInvested, int ActiveCount)> GetActiveTotalsAsync(Guid investorId, CancellationToken ct)
    {
        var active = Set.AsNoTracking()
            .Where(i => i.InvestorId == investorId && i.Status == InvestmentStatus.Active);

        var totalInvested = await active.SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
        var activeCount = await active.CountAsync(ct);
        return (totalInvested, activeCount);
    }

    public async Task<IReadOnlyList<(Guid InvestorId, long TokenCount, KycProfile? Kyc)>>
        GetActiveByPropertyAsync(Guid propertyId, CancellationToken ct)
    {
        // Active investments in the property + the investor's KYC. The KycProfile entity is
        // MATERIALIZED (not its raw column) so the value converter decrypts FullName in-memory;
        // aggregation happens in the handler.
        var rows = await (
            from i in Db.Investments.AsNoTracking()
            join k in Db.KycProfiles.AsNoTracking() on i.InvestorId equals k.UserId into kj
            from k in kj.DefaultIfEmpty()
            where i.PropertyId == propertyId && i.Status == InvestmentStatus.Active
            select new { i.InvestorId, i.TokenCount, k }).ToListAsync(ct);

        return rows.Select(r => (r.InvestorId, r.TokenCount, (KycProfile?)r.k)).ToList();
    }

    public async Task<IReadOnlyList<(Guid PropertyId, string PropertyName, long TokenCount, decimal Amount, string Currency, long TotalTokens)>>
        GetActiveHoldingsByInvestorAsync(Guid investorId, CancellationToken ct)
    {
        // The investor's Active investments joined to their property. TotalTokens is carried out so the
        // handler can compute the share without a second round-trip; the share itself is domain logic.
        var rows = await (
            from i in Db.Investments.AsNoTracking()
            join p in Db.Properties.AsNoTracking() on i.PropertyId equals p.Id
            where i.InvestorId == investorId && i.Status == InvestmentStatus.Active
            select new
            {
                i.PropertyId,
                PropertyName = p.Name,
                i.TokenCount,
                i.Amount,
                i.Currency,
                p.TotalTokens
            }).ToListAsync(ct);

        return rows
            .Select(r => (r.PropertyId, r.PropertyName, r.TokenCount, r.Amount, r.Currency, r.TotalTokens))
            .ToList();
    }
}
