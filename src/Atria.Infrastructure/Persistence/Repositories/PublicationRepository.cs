using Atria.Application.Abstractions;
using Atria.Domain.Publications;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class PublicationRepository : Repository<Publication>, IPublicationRepository
{
    public PublicationRepository(AtriaDbContext db) : base(db) { }

    public async Task<(IReadOnlyList<(Publication Publication, string? PropertyName)> Items, int TotalCount)>
        GetPageAsync(PublicationFilter filter, int page, int pageSize, CancellationToken ct)
    {
        var query = Filtered(filter);

        var totalCount = await query.CountAsync(ct);

        // LEFT JOIN properties for the denormalized name (null for general items), newest first.
        var rows = await (
            from p in query
            join prop in Db.Properties.AsNoTracking() on p.PropertyId equals prop.Id into pj
            from prop in pj.DefaultIfEmpty()
            orderby p.PublishedAtUtc descending
            select new { Publication = p, PropertyName = prop != null ? prop.Name : null })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows
            .Select(r => (r.Publication, r.PropertyName))
            .ToList();

        return (items, totalCount);
    }

    public async Task<(Publication Publication, string? PropertyName)?> GetByIdWithPropertyAsync(
        Guid id, CancellationToken ct)
    {
        var row = await (
            from p in Set.AsNoTracking()
            join prop in Db.Properties.AsNoTracking() on p.PropertyId equals prop.Id into pj
            from prop in pj.DefaultIfEmpty()
            where p.Id == id
            select new { Publication = p, PropertyName = prop != null ? prop.Name : null })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : (row.Publication, row.PropertyName);
    }

    private IQueryable<Publication> Filtered(PublicationFilter filter)
    {
        var query = Set.AsNoTracking();

        if (filter.PublishedOnly)
            query = query.Where(p => p.Status == PublicationStatus.Published);

        // GeneralOnly (no property attached) and PropertyId are mutually exclusive filters.
        if (filter.GeneralOnly)
            query = query.Where(p => p.PropertyId == null);
        else if (filter.PropertyId is { } propertyId)
            query = query.Where(p => p.PropertyId == propertyId);

        if (filter.Type is { } type)
            query = query.Where(p => p.Type == type);

        return query;
    }
}
