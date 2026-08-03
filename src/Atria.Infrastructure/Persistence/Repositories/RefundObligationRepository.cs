using Atria.Application.Abstractions;
using Atria.Domain.Refunds;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class RefundObligationRepository : Repository<RefundObligation>, IRefundObligationRepository
{
    public RefundObligationRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<RefundObligation>> ListByPropertyAsync(Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(r => r.PropertyId == propertyId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RefundObligation>> ListPendingAsync(CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Pending)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);
}
