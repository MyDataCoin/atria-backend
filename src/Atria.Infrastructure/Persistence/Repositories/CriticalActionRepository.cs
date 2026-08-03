using Atria.Application.Abstractions;
using Atria.Domain.Governance;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class CriticalActionRepository : Repository<CriticalAction>, ICriticalActionRepository
{
    public CriticalActionRepository(AtriaDbContext db) : base(db) { }

    // Tracked: the approve/reject handlers decide on the returned request and persist it.
    public Task<CriticalAction?> FindPendingAsync(CriticalActionKind kind, Guid targetId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(
            a => a.Kind == kind && a.TargetId == targetId && a.Status == CriticalActionStatus.Pending, ct);

    public async Task<IReadOnlyList<CriticalAction>> ListPendingAsync(CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(a => a.Status == CriticalActionStatus.Pending)
            .OrderBy(a => a.RequestedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CriticalAction>> ListDecidedAsync(int take, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(a => a.Status != CriticalActionStatus.Pending)
            .OrderByDescending(a => a.DecidedAtUtc)
            .Take(take)
            .ToListAsync(ct);
}
