using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class InvestmentRepository : Repository<Investment>, IInvestmentRepository
{
    public InvestmentRepository(AtriaDbContext db) : base(db) { }

    // Include payments: callers act on the full aggregate (e.g. confirm/fail payment).
    public override Task<Investment?> GetByIdAsync(Guid id, CancellationToken ct)
        => Set.Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id, ct);

    // Read-only list view; callers project scalar fields only and never read Payments.
    public async Task<IReadOnlyList<Investment>> GetByInvestorAsync(Guid investorId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(i => i.InvestorId == investorId)
            .ToListAsync(ct);

    public async Task<(decimal TotalInvested, int ActiveCount)> GetActiveTotalsAsync(Guid investorId, CancellationToken ct)
    {
        var active = Set.AsNoTracking()
            .Where(i => i.InvestorId == investorId && i.Status == InvestmentStatus.Active);

        var totalInvested = await active.SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
        var activeCount = await active.CountAsync(ct);
        return (totalInvested, activeCount);
    }

    public async Task<IReadOnlyList<(Guid InvestorId, decimal Amount, decimal TokenPrice, KycProfile? Kyc)>>
        GetActiveByPropertyAsync(Guid propertyId, CancellationToken ct)
    {
        // Active investments in the property + the property's token price + the investor's KYC.
        // The KycProfile entity is MATERIALIZED (not its raw column) so the value converter
        // decrypts FullName in-memory; aggregation happens in the handler.
        var rows = await (
            from i in Db.Investments.AsNoTracking()
            join p in Db.Properties.AsNoTracking() on i.PropertyId equals p.Id
            join k in Db.KycProfiles.AsNoTracking() on i.InvestorId equals k.UserId into kj
            from k in kj.DefaultIfEmpty()
            where i.PropertyId == propertyId && i.Status == InvestmentStatus.Active
            select new { i.InvestorId, i.Amount, p.TokenPrice, k }).ToListAsync(ct);

        return rows.Select(r => (r.InvestorId, r.Amount, r.TokenPrice, (KycProfile?)r.k)).ToList();
    }
}
