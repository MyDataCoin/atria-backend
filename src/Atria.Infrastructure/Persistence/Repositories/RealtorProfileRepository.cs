using Atria.Application.Abstractions;
using Atria.Domain.Realtors;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class RealtorProfileRepository : Repository<RealtorProfile>, IRealtorProfileRepository
{
    public RealtorProfileRepository(AtriaDbContext db) : base(db) { }

    public Task<RealtorProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct)
        => Set.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
}
