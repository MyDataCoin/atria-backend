using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class PropertyRepository : Repository<Property>, IPropertyRepository
{
    public PropertyRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<Property>> GetAllAsync(CancellationToken ct)
        => await Set.AsNoTracking().ToListAsync(ct);
}
