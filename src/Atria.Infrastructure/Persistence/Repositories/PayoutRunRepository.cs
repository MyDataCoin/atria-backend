using Atria.Application.Abstractions;
using Atria.Domain.Payouts;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class PayoutRunRepository : Repository<PayoutRun>, IPayoutRunRepository
{
    public PayoutRunRepository(AtriaDbContext db) : base(db) { }

    // Tracked: settling a line mutates the aggregate.
    public Task<PayoutRun?> GetWithItemsAsync(Guid id, CancellationToken ct)
        => Set.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<PayoutRun>> ListByPropertyAsync(Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(r => r.PropertyId == propertyId)
            .OrderByDescending(r => r.SnapshotAtUtc)
            .ToListAsync(ct);

    public Task<PayoutRun?> FindBySnapshotAsync(Guid snapshotId, CancellationToken ct)
        => Set.AsNoTracking().FirstOrDefaultAsync(r => r.SnapshotId == snapshotId, ct);

    public async Task<IReadOnlyList<(PayoutRun Run, PayoutItem Item)>> ListForInvestorAsync(
        Guid investorId, CancellationToken ct)
    {
        var rows = await Set.AsNoTracking()
            .SelectMany(r => r.Items.Where(i => i.InvestorId == investorId), (r, i) => new { Run = r, Item = i })
            .OrderByDescending(x => x.Run.SnapshotAtUtc)
            .ToListAsync(ct);

        return rows.Select(x => (x.Run, x.Item)).ToList();
    }
}
