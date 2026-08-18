using Atria.Application.Abstractions;
using Atria.Domain.Whitelist;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class MintListRepository : Repository<MintList>, IMintListRepository
{
    public MintListRepository(AtriaDbContext db) : base(db) { }

    public Task<MintList?> GetWithItemsAsync(Guid id, CancellationToken ct)
        => Set.AsNoTracking()
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<IReadOnlyList<(MintList List, string? PropertyName)>> ListAsync(
        Guid? propertyId, CancellationToken ct)
    {
        var query =
            from list in Set.AsNoTracking()
            join property in Db.Properties.AsNoTracking() on list.PropertyId equals property.Id into props
            from property in props.DefaultIfEmpty()
            select new { List = list, PropertyName = (string?)property.Name };

        if (propertyId is not null)
            query = query.Where(r => r.List.PropertyId == propertyId);

        var rows = await query.OrderByDescending(r => r.List.CreatedAtUtc).ToListAsync(ct);

        return rows.Select(r => (r.List, r.PropertyName)).ToList();
    }

    public Task<int> CountForYearAsync(int year, CancellationToken ct)
        => Set.AsNoTracking().CountAsync(l => l.CreatedAtUtc.Year == year, ct);
}
