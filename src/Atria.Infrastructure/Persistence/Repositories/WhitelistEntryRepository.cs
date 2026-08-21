using Atria.Application.Abstractions;
using Atria.Domain.Whitelist;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class WhitelistEntryRepository : Repository<WhitelistEntry>, IWhitelistEntryRepository
{
    public WhitelistEntryRepository(AtriaDbContext db) : base(db) { }

    public Task<WhitelistEntry?> GetByInvestmentAsync(Guid investmentId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(e => e.InvestmentId == investmentId, ct);

    public async Task<IReadOnlyList<(WhitelistEntry Entry, string? PropertyName, string? MintListNumber)>>
        ListForOperatorAsync(Guid? propertyId, WhitelistStatus? status, int take, CancellationToken ct)
    {
        // Left-joined to the issuance and the holding batch so the operator's table needs one round
        // trip. Untracked: this read only ever renders.
        var query =
            from entry in Set.AsNoTracking()
            join property in Db.Properties.AsNoTracking() on entry.PropertyId equals property.Id into props
            from property in props.DefaultIfEmpty()
            join list in Db.MintLists.AsNoTracking() on entry.MintListId equals list.Id into lists
            from list in lists.DefaultIfEmpty()
            select new { Entry = entry, PropertyName = (string?)property.Name, MintListNumber = (string?)list.Number };

        if (propertyId is not null)
            query = query.Where(r => r.Entry.PropertyId == propertyId);
        if (status is not null)
            query = query.Where(r => r.Entry.Status == status);

        var rows = await query
            .OrderByDescending(r => r.Entry.RequestedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(r => (r.Entry, r.PropertyName, r.MintListNumber)).ToList();
    }

    // Tracked: the caller transitions these into a batch.
    public async Task<IReadOnlyList<WhitelistEntry>> GetManyAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => await Set.Where(e => ids.Contains(e.Id)).ToListAsync(ct);

    // Tracked: the caller writes the address onto these.
    public async Task<IReadOnlyList<WhitelistEntry>> ListAwaitingWalletAsync(
        Guid investorId, CancellationToken ct)
        => await Set
            .Where(e => e.InvestorId == investorId
                        && e.WalletAddress == null
                        && (e.Status == WhitelistStatus.Pending || e.Status == WhitelistStatus.Ready))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WhitelistEntry>> ListByMintListAsync(Guid mintListId, CancellationToken ct)
        => await Set.Where(e => e.MintListId == mintListId).ToListAsync(ct);
}
