using Atria.Application.Abstractions;
using Atria.Domain.Trading;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class ExchangeTradeRepository : Repository<ExchangeTrade>, IExchangeTradeRepository
{
    public ExchangeTradeRepository(AtriaDbContext db) : base(db) { }

    // Tracked: the idempotency check is followed by a matcher that mutates what it finds.
    public Task<ExchangeTrade?> FindByEventIdAsync(string eventId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(t => t.EventId == eventId, ct);

    // Tracked, and restricted to settlements: a reversal names the trade it takes back, and the
    // caller marks that trade reversed.
    public Task<ExchangeTrade?> FindSettlementByEventIdAsync(string eventId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(
            t => t.EventId == eventId && t.EventType == ExchangeTradeType.Settled, ct);

    public async Task<IReadOnlyList<ExchangeTrade>> ListByTransactionAsync(
        string transactionHash, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(t => t.TransactionHash == transactionHash)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExchangeTrade>> ListByPropertyAsync(
        Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(t => t.PropertyId == propertyId)
            .OrderByDescending(t => t.SettledAtUtc)
            .ThenByDescending(t => t.SequenceNo)
            .ToListAsync(ct);

    // Tracked: the matcher writes the verdict back onto each row it examines.
    public async Task<IReadOnlyList<ExchangeTrade>> ListAwaitingChainAsync(
        Guid propertyId, CancellationToken ct)
        => await Set
            .Where(t => t.PropertyId == propertyId && t.MatchState == TradeMatchState.AwaitingChain)
            .OrderBy(t => t.SequenceNo)
            .ToListAsync(ct);

    public async Task<long> GetHighestSequenceAsync(Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(t => t.PropertyId == propertyId)
            .Select(t => (long?)t.SequenceNo)
            .MaxAsync(ct) ?? 0;

    public async Task<IReadOnlyList<long>> ListSequencesAsync(Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(t => t.PropertyId == propertyId)
            .Select(t => t.SequenceNo)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);
}
