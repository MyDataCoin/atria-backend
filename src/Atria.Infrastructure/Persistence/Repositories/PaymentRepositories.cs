using Atria.Application.Abstractions;
using Atria.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class PaymentSettingsRepository : IPaymentSettingsRepository
{
    private readonly AtriaDbContext _db;

    public PaymentSettingsRepository(AtriaDbContext db) => _db = db;

    public Task<PaymentSettings?> GetAsync(CancellationToken ct)
        => _db.Set<PaymentSettings>().FirstOrDefaultAsync(s => s.Id == PaymentSettings.SingletonId, ct);

    public async Task AddAsync(PaymentSettings settings, CancellationToken ct)
        => await _db.Set<PaymentSettings>().AddAsync(settings, ct);
}

public sealed class CryptoInvoiceRepository : Repository<CryptoInvoice>, ICryptoInvoiceRepository
{
    public CryptoInvoiceRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetReceivedKgsAsync(
        IReadOnlyCollection<Guid> investmentIds, CancellationToken ct)
    {
        if (investmentIds.Count == 0)
            return new Dictionary<Guid, decimal>();

        var paid = await Set.AsNoTracking()
            .Where(i => investmentIds.Contains(i.InvestmentId)
                        && i.Status == CryptoInvoiceStatus.Paid
                        && i.ReceivedAmount != null)
            .Select(i => new { i.InvestmentId, Received = i.ReceivedAmount!.Value, i.RateKgs })
            .ToListAsync(ct);

        return paid
            .GroupBy(x => x.InvestmentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => decimal.Round(x.Received * x.RateKgs, 2)));
    }

    public Task<CryptoInvoice?> GetLatestByInvestmentAsync(Guid investmentId, CancellationToken ct)
        => Set.AsNoTracking()
            .Where(i => i.InvestmentId == investmentId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CryptoInvoice>> GetPendingByInvestmentAsync(Guid investmentId, CancellationToken ct)
        => await Set.Where(i => i.InvestmentId == investmentId && i.Status == CryptoInvoiceStatus.Pending)
            .ToListAsync(ct);

    public async Task<IReadOnlySet<decimal>> GetPendingAmountsAsync(CryptoPaymentMethod method, CancellationToken ct)
        => (await Set.AsNoTracking()
                .Where(i => i.Method == method && i.Status == CryptoInvoiceStatus.Pending)
                .Select(i => i.PayAmount)
                .ToListAsync(ct))
            .ToHashSet();

    public Task<CryptoInvoice?> FindPendingByAmountAsync(
        CryptoPaymentMethod method, string payToAddress, decimal amount, CancellationToken ct)
    {
        // EVM addresses come back in any case (checksummed or not); compare case-insensitively.
        var address = payToAddress.ToLower();
        return Set.FirstOrDefaultAsync(
            i => i.Method == method
                 && i.Status == CryptoInvoiceStatus.Pending
                 && i.PayAmount == amount
                 && i.PayToAddress.ToLower() == address,
            ct);
    }

    public async Task<IReadOnlyList<CryptoInvoice>> ListAsync(CryptoInvoiceStatus? status, int take, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(i => status == null || i.Status == status)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
}

public sealed class CryptoDepositRepository : Repository<CryptoDeposit>, ICryptoDepositRepository
{
    public CryptoDepositRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlySet<string>> GetExistingKeysAsync(
        IReadOnlyCollection<string> transferKeys, CancellationToken ct)
        => (await Set.AsNoTracking()
                .Where(d => transferKeys.Contains(d.TransferKey))
                .Select(d => d.TransferKey)
                .ToListAsync(ct))
            .ToHashSet();

    public async Task<IReadOnlyList<CryptoDeposit>> ListAsync(bool unmatchedOnly, int take, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(d => !unmatchedOnly || d.InvoiceId == null)
            .OrderByDescending(d => d.OccurredAtUtc)
            .Take(take)
            .ToListAsync(ct);
}

public sealed class CryptoWatchCursorRepository : Repository<CryptoWatchCursor>, ICryptoWatchCursorRepository
{
    public CryptoWatchCursorRepository(AtriaDbContext db) : base(db) { }

    public Task<CryptoWatchCursor?> FindAsync(string key, CancellationToken ct)
        => Set.FirstOrDefaultAsync(c => c.Key == key, ct);
}
