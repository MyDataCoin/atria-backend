using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Atria.Domain.Payments;

namespace Atria.Application.Payments.Services;

/// <summary>
/// Records transfers read off a chain and settles the invoices they pay.
/// </summary>
/// <remarks>
/// A transfer pays an invoice only when everything lines up: same method, same wallet, exactly the
/// invoiced amount, sent before the invoice lapsed, and the application still awaiting payment. Anything
/// else is recorded unmatched for the operator — an approximate match would credit the wrong person the
/// day two investors send similar amounts. Already-recorded transfers are skipped, so re-reading a range
/// is harmless. Each transfer is saved on its own: two transfers of the same amount in one batch must
/// not both settle the one invoice.
/// </remarks>
public sealed class CryptoDepositProcessor
{
    private readonly ICryptoDepositRepository _deposits;
    private readonly ICryptoInvoiceRepository _invoices;
    private readonly IInvestmentRepository _investments;
    private readonly IUnitOfWork _unitOfWork;

    public CryptoDepositProcessor(
        ICryptoDepositRepository deposits,
        ICryptoInvoiceRepository invoices,
        IInvestmentRepository investments,
        IUnitOfWork unitOfWork)
    {
        _deposits = deposits;
        _invoices = invoices;
        _investments = investments;
        _unitOfWork = unitOfWork;
    }

    public sealed record Outcome(int Recorded, int Matched);

    public async Task<Outcome> ProcessAsync(
        CryptoPaymentMethod method, IReadOnlyList<IncomingTransfer> transfers, CancellationToken ct)
    {
        if (transfers.Count == 0)
            return new Outcome(0, 0);

        var known = await _deposits.GetExistingKeysAsync(transfers.Select(t => t.TransferKey).ToList(), ct);
        var recorded = 0;
        var matched = 0;

        foreach (var transfer in transfers)
        {
            if (known.Contains(transfer.TransferKey) || transfer.Amount <= 0)
                continue;

            var deposit = CryptoDeposit.Record(
                method, transfer.TransferKey, transfer.TransactionHash, transfer.FromAddress, transfer.ToAddress,
                transfer.Amount, transfer.OccurredAtUtc);

            var invoice = await _invoices.FindPendingByAmountAsync(method, transfer.ToAddress, transfer.Amount, ct);
            if (invoice is not null && transfer.OccurredAtUtc <= invoice.ExpiresAtUtc)
            {
                var investment = await _investments.GetByIdAsync(invoice.InvestmentId, ct);
                if (investment is { Status: InvestmentStatus.Reserved })
                {
                    invoice.MarkPaid(transfer.TransactionHash, transfer.FromAddress, transfer.OccurredAtUtc);
                    _invoices.Update(invoice);
                    deposit.MatchTo(invoice.Id);
                    matched++;
                }
            }

            await _deposits.AddAsync(deposit, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            known = new HashSet<string>(known) { transfer.TransferKey };
            recorded++;
        }

        return new Outcome(recorded, matched);
    }
}
