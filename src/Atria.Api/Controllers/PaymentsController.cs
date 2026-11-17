using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Payments.Commands;
using Atria.Application.Payments.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Crypto payments: operator settings, offered methods, per-application invoices, incoming transfers.</summary>
/// <remarks>
/// Flow: the investor reserves an application (<c>POST /investments</c>), picks a method here and gets an
/// invoice — wallet, network and an exact amount whose last decimals identify the application. A watcher
/// reads the wallets on-chain and marks the invoice paid when that exact amount arrives; the operator then
/// approves the application as for any other payment (<c>POST /investments/{id}/approve</c>).
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
[Authorize]
public sealed class PaymentsController : ApiControllerBase
{
    public PaymentsController(ISender sender) : base(sender) { }

    /// <summary>Crypto methods offered right now, with the rate each converts the som price at.</summary>
    [HttpGet("methods")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<PaymentMethodDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Methods(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetPaymentMethodsQuery(), ct));

    /// <summary>The operator's crypto settings: USDT rate, receiving wallets, KGST contract.</summary>
    [HttpGet("settings")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [ProducesResponseType<PaymentSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetPaymentSettingsQuery(), ct));

    /// <summary>Replaces the crypto settings. An empty field switches off the methods that need it.</summary>
    /// <remarks>
    /// The rate applies to invoices issued from now on; invoices already issued keep the rate they were
    /// quoted at.
    /// </remarks>
    [HttpPut("settings")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [ProducesResponseType<PaymentSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSettings(UpdatePaymentSettingsRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new UpdatePaymentSettingsCommand(
            request.UsdtRateKgs, request.Bep20Address, request.Trc20Address, request.KgstContractAddress), ct));

    /// <summary>Issues the crypto invoice for one of the caller's reserved applications.</summary>
    /// <remarks>
    /// <c>method</c> is <c>usdt_bep20</c>, <c>usdt_trc20</c> or <c>kgst_bep20</c>. Asking again for the same
    /// method returns the same invoice; another method replaces it. 409 when the application is not
    /// awaiting payment, is already paid, or the method is switched off.
    /// </remarks>
    [HttpPost("investments/{investmentId:guid}/invoice")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<CryptoInvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInvoice(
        Guid investmentId, CreateCryptoInvoiceRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new CreateCryptoInvoiceCommand(investmentId, request.Method), ct));

    /// <summary>The application's latest crypto invoice — poll it to see the payment arrive.</summary>
    [HttpGet("investments/{investmentId:guid}/invoice")]
    [Authorize(Roles = "Investor,Admin,SuperAdmin")]
    [ProducesResponseType<CryptoInvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoice(Guid investmentId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetCryptoInvoiceQuery(investmentId), ct));

    /// <summary>Crypto invoices, newest first. <c>status</c>: pending, paid or cancelled.</summary>
    [HttpGet("invoices")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [ProducesResponseType<IReadOnlyList<CryptoInvoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInvoices([FromQuery] string? status, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ListCryptoInvoicesQuery(status), ct));

    /// <summary>Transfers seen on the receiving wallets. <c>unmatched=true</c> leaves the ones needing a human.</summary>
    [HttpGet("deposits")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [ProducesResponseType<IReadOnlyList<CryptoDepositDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDeposits([FromQuery] bool unmatched, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ListCryptoDepositsQuery(unmatched), ct));
}

public sealed record UpdatePaymentSettingsRequest(
    decimal? UsdtRateKgs, string? Bep20Address, string? Trc20Address, string? KgstContractAddress);

public sealed record CreateCryptoInvoiceRequest(string Method);
