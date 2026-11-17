using System.Security.Cryptography;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Payments.Dtos;
using Atria.Domain.Investments;
using Atria.Domain.Payments;
using Atria.Domain.Users;

namespace Atria.Application.Payments.Commands;

/// <summary>
/// Issues (or re-issues) the crypto invoice for one of the investor's reserved applications.
/// </summary>
/// <remarks>
/// Picking another method replaces the pending invoice: an application is paid once, in one token.
/// Asking again for the SAME method returns the pending invoice unchanged, so a reload of the payment
/// screen never moves the amount the investor may already be sending.
/// </remarks>
public sealed record CreateCryptoInvoiceCommand(Guid InvestmentId, string Method) : IRequest<Result<CryptoInvoiceDto>>;

public sealed class CreateCryptoInvoiceCommandHandler
    : IRequestHandler<CreateCryptoInvoiceCommand, Result<CryptoInvoiceDto>>
{
    // Two decimals of fingerprint give 99 distinct amounts per method at a time; past that the
    // fingerprint spills into whole units (still under a dollar's worth per hundred applications).
    private const int MaxFingerprint = 9_999;

    private readonly IInvestmentRepository _investments;
    private readonly ICryptoInvoiceRepository _invoices;
    private readonly IPaymentSettingsRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public CreateCryptoInvoiceCommandHandler(
        IInvestmentRepository investments,
        ICryptoInvoiceRepository invoices,
        IPaymentSettingsRepository settings,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _investments = investments;
        _invoices = invoices;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<CryptoInvoiceDto>> Handle(CreateCryptoInvoiceCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<CryptoInvoiceDto>(Error.Unauthorized("payments.unauthorized", "Authentication required."));

        if (!CryptoPaymentMethodExtensions.TryParseWire(request.Method, out var method))
            return Result.Failure<CryptoInvoiceDto>(Error.Validation(
                "payments.unknown_method", "Method must be one of usdt_bep20, usdt_trc20, kgst_bep20."));

        // Someone else's application reads as missing: its existence is not the caller's business.
        var investment = await _investments.GetByIdAsync(request.InvestmentId, ct);
        if (investment is null || investment.InvestorId != userId)
            return Result.Failure<CryptoInvoiceDto>(Error.NotFound("payments.investment_not_found", "Application not found."));

        if (investment.Status != InvestmentStatus.Reserved || investment.ReservedUntilUtc <= _clock.UtcNow)
            return Result.Failure<CryptoInvoiceDto>(Error.Conflict(
                "payments.investment_not_payable", "This application is no longer awaiting payment."));

        var settings = await _settings.GetAsync(ct);
        if (settings is null || !settings.IsEnabled(method))
            return Result.Failure<CryptoInvoiceDto>(Error.Conflict(
                "payments.method_unavailable", "This payment method is not available right now."));

        var pending = await _invoices.GetPendingByInvestmentAsync(investment.Id, ct);
        var same = pending.FirstOrDefault(i => i.Method == method && i.PayToAddress == settings.AddressFor(method));
        if (same is not null)
            return Result.Success(CryptoInvoiceDto.From(same));

        // A paid invoice settles the application; another one would invite paying twice.
        var latest = await _invoices.GetLatestByInvestmentAsync(investment.Id, ct);
        if (latest is { Status: CryptoInvoiceStatus.Paid })
            return Result.Failure<CryptoInvoiceDto>(Error.Conflict(
                "payments.already_paid", "This application has already been paid."));

        foreach (var old in pending)
        {
            old.Cancel();
            _invoices.Update(old);
        }

        var rate = settings.RateFor(method)!.Value;
        var taken = await _invoices.GetPendingAmountsAsync(method, ct);
        var baseAmount = CryptoInvoice.BaseAmount(investment.Amount, rate);
        var fingerprint = PickFingerprint(baseAmount, taken);
        if (fingerprint is null)
            return Result.Failure<CryptoInvoiceDto>(Error.Conflict(
                "payments.busy", "Too many open payments in this method right now — try another or retry shortly."));

        var invoice = CryptoInvoice.Issue(
            investment.Id, userId, method, settings.AddressFor(method)!, investment.Amount, rate,
            fingerprint.Value, investment.ReservedUntilUtc);

        await _invoices.AddAsync(invoice, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(CryptoInvoiceDto.From(invoice));
    }

    /// <summary>A random free fingerprint: random so amounts reveal nothing about order or volume.</summary>
    public static int? PickFingerprint(decimal baseAmount, IReadOnlySet<decimal> taken)
    {
        // Small range first, so the investor overpays by cents, not units.
        foreach (var ceiling in new[] { 99, 999, MaxFingerprint })
        {
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var candidate = RandomNumberGenerator.GetInt32(1, ceiling + 1);
                if (!taken.Contains(baseAmount + candidate / 100m))
                    return candidate;
            }
        }

        return null;
    }
}

/// <summary>The latest crypto invoice of an application — the owner's or, for an admin, anyone's.</summary>
public sealed record GetCryptoInvoiceQuery(Guid InvestmentId) : IRequest<Result<CryptoInvoiceDto>>;

public sealed class GetCryptoInvoiceQueryHandler : IRequestHandler<GetCryptoInvoiceQuery, Result<CryptoInvoiceDto>>
{
    private readonly ICryptoInvoiceRepository _invoices;
    private readonly ICurrentUserService _currentUser;

    public GetCryptoInvoiceQueryHandler(ICryptoInvoiceRepository invoices, ICurrentUserService currentUser)
    {
        _invoices = invoices;
        _currentUser = currentUser;
    }

    public async Task<Result<CryptoInvoiceDto>> Handle(GetCryptoInvoiceQuery request, CancellationToken ct)
    {
        var invoice = await _invoices.GetLatestByInvestmentAsync(request.InvestmentId, ct);
        var isStaff = _currentUser.IsInRole(Role.Admin) || _currentUser.IsInRole(Role.SuperAdmin);

        if (invoice is null || (!isStaff && invoice.InvestorId != _currentUser.UserId))
            return Result.Failure<CryptoInvoiceDto>(Error.NotFound("payments.invoice_not_found", "No crypto invoice for this application."));

        return Result.Success(CryptoInvoiceDto.From(invoice));
    }
}

/// <summary>Crypto invoices for the operator, newest first.</summary>
public sealed record ListCryptoInvoicesQuery(string? Status) : IRequest<Result<IReadOnlyList<CryptoInvoiceDto>>>;

public sealed class ListCryptoInvoicesQueryHandler
    : IRequestHandler<ListCryptoInvoicesQuery, Result<IReadOnlyList<CryptoInvoiceDto>>>
{
    private readonly ICryptoInvoiceRepository _invoices;

    public ListCryptoInvoicesQueryHandler(ICryptoInvoiceRepository invoices) => _invoices = invoices;

    public async Task<Result<IReadOnlyList<CryptoInvoiceDto>>> Handle(ListCryptoInvoicesQuery request, CancellationToken ct)
    {
        CryptoInvoiceStatus? status = request.Status?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "pending" => CryptoInvoiceStatus.Pending,
            "paid" => CryptoInvoiceStatus.Paid,
            "cancelled" => CryptoInvoiceStatus.Cancelled,
            _ => (CryptoInvoiceStatus)(-1)
        };
        if (status is { } s && !Enum.IsDefined(s))
            return Result.Failure<IReadOnlyList<CryptoInvoiceDto>>(Error.Validation(
                "payments.unknown_status", "Status must be pending, paid or cancelled."));

        IReadOnlyList<CryptoInvoiceDto> list = (await _invoices.ListAsync(status, 500, ct))
            .Select(CryptoInvoiceDto.From).ToList();
        return Result.Success(list);
    }
}

/// <summary>Transfers seen on the receiving wallets; <c>unmatchedOnly</c> leaves what needs a human.</summary>
public sealed record ListCryptoDepositsQuery(bool UnmatchedOnly) : IRequest<Result<IReadOnlyList<CryptoDepositDto>>>;

public sealed class ListCryptoDepositsQueryHandler
    : IRequestHandler<ListCryptoDepositsQuery, Result<IReadOnlyList<CryptoDepositDto>>>
{
    private readonly ICryptoDepositRepository _deposits;

    public ListCryptoDepositsQueryHandler(ICryptoDepositRepository deposits) => _deposits = deposits;

    public async Task<Result<IReadOnlyList<CryptoDepositDto>>> Handle(ListCryptoDepositsQuery request, CancellationToken ct)
    {
        IReadOnlyList<CryptoDepositDto> list = (await _deposits.ListAsync(request.UnmatchedOnly, 500, ct))
            .Select(CryptoDepositDto.From).ToList();
        return Result.Success(list);
    }
}
