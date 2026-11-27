using Atria.Domain.Payments;

namespace Atria.Application.Abstractions;

public interface IPaymentSettingsRepository
{
    /// <summary>The settings row (tracked), or null when nobody has saved settings yet.</summary>
    Task<PaymentSettings?> GetAsync(CancellationToken ct);

    Task AddAsync(PaymentSettings settings, CancellationToken ct);
}

public interface ICryptoInvoiceRepository : IRepository<CryptoInvoice>
{
    /// <summary>The investment's most recent invoice, any status.</summary>
    Task<CryptoInvoice?> GetLatestByInvestmentAsync(Guid investmentId, CancellationToken ct);

    /// <summary>Pending invoices of one investment (tracked) — normally zero or one.</summary>
    Task<IReadOnlyList<CryptoInvoice>> GetPendingByInvestmentAsync(Guid investmentId, CancellationToken ct);

    /// <summary>Amounts already taken by pending invoices of a method — the fingerprints to avoid.</summary>
    Task<IReadOnlySet<decimal>> GetPendingAmountsAsync(CryptoPaymentMethod method, CancellationToken ct);

    /// <summary>The pending invoice of this method and wallet asking for exactly this amount (tracked).</summary>
    Task<CryptoInvoice?> FindPendingByAmountAsync(
        CryptoPaymentMethod method, string payToAddress, decimal amount, CancellationToken ct);

    Task<IReadOnlyList<CryptoInvoice>> ListAsync(CryptoInvoiceStatus? status, int take, CancellationToken ct);

    /// <summary>
    /// For each of these investments paid by crypto invoice: what actually arrived, in som. An
    /// investment missing from the result was not paid through an invoice (a bank transfer, say).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetReceivedKgsAsync(
        IReadOnlyCollection<Guid> investmentIds, CancellationToken ct);
}

public interface ICryptoDepositRepository : IRepository<CryptoDeposit>
{
    /// <summary>Which of these transfer keys are already recorded.</summary>
    Task<IReadOnlySet<string>> GetExistingKeysAsync(IReadOnlyCollection<string> transferKeys, CancellationToken ct);

    Task<IReadOnlyList<CryptoDeposit>> ListAsync(bool unmatchedOnly, int take, CancellationToken ct);
}

public interface ICryptoWatchCursorRepository : IRepository<CryptoWatchCursor>
{
    /// <summary>The cursor for a key (tracked), or null when the pair was never read.</summary>
    Task<CryptoWatchCursor?> FindAsync(string key, CancellationToken ct);
}
