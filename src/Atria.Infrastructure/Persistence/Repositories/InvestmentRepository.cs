using Atria.Application.Abstractions;
using Atria.Domain.Investments;
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
}
