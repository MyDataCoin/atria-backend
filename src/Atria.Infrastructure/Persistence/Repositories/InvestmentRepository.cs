using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;
using Atria.Domain.Trading;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class InvestmentRepository : Repository<Investment>, IInvestmentRepository
{
    public InvestmentRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyDictionary<Guid, long>> GetActiveTokenTotalsByInvestorAsync(CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(i => i.Status == InvestmentStatus.Active)
            .GroupBy(i => i.InvestorId)
            .Select(g => new { g.Key, Total = g.Sum(i => i.TokenCount) })
            .ToDictionaryAsync(x => x.Key, x => (long)x.Total, ct);

    // Read-only list view; callers project scalar fields only.
    public async Task<IReadOnlyList<Investment>> GetByInvestorAsync(Guid investorId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(i => i.InvestorId == investorId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Investment>> ListAsync(
        InvestmentStatus? status, Guid? propertyId, int take, CancellationToken ct)
    {
        var query = Set.AsNoTracking().AsQueryable();

        if (status is not null)
            query = query.Where(i => i.Status == status);
        if (propertyId is not null)
            query = query.Where(i => i.PropertyId == propertyId);

        // Newest first: the operator works the top of the queue, and a reservation that lapses drops
        // out of the Reserved filter on its own.
        return await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Investment>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return Array.Empty<Investment>();

        return await Set.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Investment>> GetExpiredReservationsAsync(
        DateTime asOfUtc, int batchSize, CancellationToken ct)
        // Tracked (no AsNoTracking): the sweep mutates and persists these through the unit of work.
        // Ordered oldest-first and capped so a large backlog is drained across ticks, not all at once.
        => await Set
            .Where(i => i.Status == InvestmentStatus.Reserved && i.ReservedUntilUtc <= asOfUtc)
            .OrderBy(i => i.ReservedUntilUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<(decimal TotalInvested, int ActiveCount)> GetActiveTotalsAsync(Guid investorId, CancellationToken ct)
    {
        var active = Set.AsNoTracking()
            .Where(i => i.InvestorId == investorId && i.Status == InvestmentStatus.Active);

        var totalInvested = await active.SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
        var activeCount = await active.CountAsync(ct);
        return (totalInvested, activeCount);
    }

    public async Task<IReadOnlyList<(Guid InvestorId, long TokenCount, KycProfile? Kyc)>>
        GetActiveByPropertyAsync(Guid propertyId, CancellationToken ct)
    {
        // Active investments in the property + the investor's KYC. The KycProfile entity is
        // MATERIALIZED (not its raw column) so the value converter decrypts FullName in-memory;
        // aggregation happens in the handler.
        var rows = await (
            from i in Db.Investments.AsNoTracking()
            join k in Db.KycProfiles.AsNoTracking() on i.InvestorId equals k.UserId into kj
            from k in kj.DefaultIfEmpty()
            where i.PropertyId == propertyId && i.Status == InvestmentStatus.Active
            select new { i.InvestorId, i.TokenCount, k }).ToListAsync(ct);

        return rows.Select(r => (r.InvestorId, r.TokenCount, (KycProfile?)r.k)).ToList();
    }

    public async Task<IReadOnlyList<(Guid PropertyId, string PropertyName, long TokenCount, decimal Amount, string Currency, long TotalTokens)>>
        GetHoldingsAtAsync(Guid investorId, DateTime cutoffUtc, CancellationToken ct)
    {
        // Placements that were held at the cutoff: activated before it, and either still Active or
        // closed (withdrawn / annulled) only at or after it.
        var placements = await Db.Investments.AsNoTracking()
            .Where(i => i.InvestorId == investorId
                        && i.ActivatedAtUtc != null && i.ActivatedAtUtc < cutoffUtc
                        && (i.Status == InvestmentStatus.Active
                            || ((i.Status == InvestmentStatus.Withdrawn || i.Status == InvestmentStatus.Annulled)
                                && i.ClosedAtUtc != null && i.ClosedAtUtc >= cutoffUtc)))
            .Select(i => new { i.PropertyId, i.TokenCount, i.Amount, At = i.ActivatedAtUtc!.Value, i.WalletAddress })
            .ToListAsync(ct);

        // Every address the investor is known by: the register resolves addresses to investors, and
        // each placement names the wallet it was minted to. Secondary trades are matched on these.
        var registerWallets = await Db.HolderPositions.AsNoTracking()
            .Where(h => h.InvestorId == investorId)
            .Select(h => h.WalletAddress)
            .ToListAsync(ct);
        var placementWallets = await Db.Investments.AsNoTracking()
            .Where(i => i.InvestorId == investorId && i.WalletAddress != null)
            .Select(i => i.WalletAddress!)
            .ToListAsync(ct);
        var wallets = registerWallets.Concat(placementWallets)
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        var trades = wallets.Count == 0
            ? []
            : (await Db.ExchangeTrades.AsNoTracking()
                .Where(t => t.EventType == ExchangeTradeType.Settled && !t.IsReversed && t.SettledAtUtc < cutoffUtc)
                .ToListAsync(ct))
              .Where(t => wallets.Contains(t.BuyerAddress.ToLowerInvariant())
                          || wallets.Contains(t.SellerAddress.ToLowerInvariant()))
              .ToList();

        // Replayed in time order per property, so a sale leaves at the average cost of what was held
        // at that moment, not at a cost that includes later purchases.
        var events = placements
            .Select(p => (p.PropertyId, p.At, Tokens: p.TokenCount, Cost: p.Amount))
            .Concat(trades.SelectMany(t =>
            {
                var bought = wallets.Contains(t.BuyerAddress.ToLowerInvariant());
                var sold = wallets.Contains(t.SellerAddress.ToLowerInvariant());
                // Moving shares between two of the investor's own wallets changes nothing.
                if (bought && sold) return Array.Empty<(Guid, DateTime, long, decimal)>();
                return bought
                    ? [(t.PropertyId, t.SettledAtUtc, t.TokenCount, t.TokenCount * t.PricePerToken)]
                    : [(t.PropertyId, t.SettledAtUtc, -t.TokenCount, 0m)];
            }))
            .OrderBy(e => e.Item2);

        var held = new Dictionary<Guid, (long Tokens, decimal Cost)>();
        foreach (var (propertyId, _, tokens, cost) in events)
        {
            held.TryGetValue(propertyId, out var h);
            if (tokens >= 0)
            {
                h = (h.Tokens + tokens, h.Cost + cost);
            }
            else
            {
                var sold = Math.Min(-tokens, h.Tokens);
                var costOut = h.Tokens == 0 ? 0m : decimal.Round(h.Cost * sold / h.Tokens, 2);
                h = (h.Tokens - sold, h.Cost - costOut);
            }
            held[propertyId] = h;
        }

        var ids = held.Where(kv => kv.Value.Tokens > 0).Select(kv => kv.Key).ToList();
        if (ids.Count == 0)
            return [];

        var properties = await Db.Properties.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Currency, p.TotalTokens })
            .ToListAsync(ct);

        return properties
            .Select(p => (p.Id, p.Name, held[p.Id].Tokens, held[p.Id].Cost, p.Currency, p.TotalTokens))
            .OrderBy(r => r.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<(Guid PropertyId, string PropertyName, long TokenCount, decimal Amount, string Currency, long TotalTokens)>>
        GetActiveHoldingsByInvestorAsync(Guid investorId, CancellationToken ct)
    {
        // The investor's Active investments joined to their property. TotalTokens is carried out so the
        // handler can compute the share without a second round-trip; the share itself is domain logic.
        var rows = await (
            from i in Db.Investments.AsNoTracking()
            join p in Db.Properties.AsNoTracking() on i.PropertyId equals p.Id
            where i.InvestorId == investorId && i.Status == InvestmentStatus.Active
            select new
            {
                i.PropertyId,
                PropertyName = p.Name,
                i.TokenCount,
                i.Amount,
                i.Currency,
                p.TotalTokens
            }).ToListAsync(ct);

        return rows
            .Select(r => (r.PropertyId, r.PropertyName, r.TokenCount, r.Amount, r.Currency, r.TotalTokens))
            .ToList();
    }
}
