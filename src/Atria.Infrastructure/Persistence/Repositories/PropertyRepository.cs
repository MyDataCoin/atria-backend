using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class PropertyRepository : Repository<Property>, IPropertyRepository
{
    public PropertyRepository(AtriaDbContext db) : base(db) { }

    // Include media so callers can read/mutate the full aggregate (add/remove images & documents).
    public override Task<Property?> GetByIdAsync(Guid id, CancellationToken ct)
        => Set.Include(p => p.Images)
              .Include(p => p.Documents)
              .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Property>> GetAllAsync(CancellationToken ct)
        => await Set.AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Documents)
            .ToListAsync(ct);
}
