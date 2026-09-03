using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Kyc.Commands;
using Atria.Application.Kyc.Dtos;
using Atria.Application.Kyc.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>KYC verification: start a session, read your status, and (Compliance) review.</summary>
/// <remarks>
/// **KYC flow (hosted provider, e.g. Didit):**
/// 1. Investor calls <c>POST /kyc/submit</c> → the backend opens a provider verification
///    session and returns a <c>verificationUrl</c>; the profile moves to <c>UnderReview</c>.
/// 2. The client redirects the user to that <c>verificationUrl</c> to complete checks
///    (document, liveness, etc.) on the provider's hosted page.
/// 3. The provider calls our webhook <c>POST /webhooks/kyc/{provider}</c> (signature-verified,
///    idempotent). A terminal <c>status.updated</c> moves the profile to <c>Approved</c> or
///    <c>Rejected</c>; non-terminal events are acknowledged without changing state.
/// 4. The client polls <c>GET /kyc/me</c> for the latest status. On <c>Approved</c>, downstream
///    effects (DID/attestations, allowlist) run asynchronously via the outbox.
///
/// **When the webhook never arrives.** Step 3 is a delivery, and deliveries are lost: Didit retries a
/// failing endpoint five times and then drops the event for good, leaving a verification the user
/// really did pass stuck in <c>UnderReview</c>. <c>POST /kyc/{id}/sync</c> asks the provider directly
/// what it decided and applies it — the way out of a stuck profile, without waiting on a redelivery
/// that is never coming.
///
/// Statuses: <c>Pending → UnderReview → Approved | Rejected</c>.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/kyc")]
[Authorize]
public sealed class KycController : ApiControllerBase
{
    public KycController(ISender sender) : base(sender) { }

    /// <summary>Step 1: the investor submits KYC and starts a provider verification session.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role. Ensures a KYC profile exists for the caller (one per
    /// user), opens a verification session with the chosen provider, moves the profile to
    /// <c>UnderReview</c>, and RETURNS the hosted <c>verificationUrl</c> — the client MUST
    /// redirect the user there to finish verification. The final decision arrives later via the
    /// provider webhook (poll <c>GET /kyc/me</c>). A supplied wallet address must be a valid
    /// 0x-prefixed 40-hex address. Resubmitting a profile already under review/approved/rejected
    /// is a domain rule violation (<c>400</c>).
    /// </remarks>
    /// <param name="request">The provider plus optional wallet address and identity details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Session started; returns profileId, status, sessionId and the verificationUrl to redirect to.</response>
    /// <response code="400">Validation failed, the provider is not configured, or the profile cannot be (re)submitted.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not an Investor.</response>
    [HttpPost("submit")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<KycSubmissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Submit(SubmitKycRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new SubmitKycCommand(
            request.Provider, request.WalletAddress, request.FullName,
            request.DocumentNumber, request.Nationality), ct);
        return ToActionResult(result);
    }

    /// <summary>Links the investor's crypto wallet to their KYC profile (after verification).</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role. By UX the wallet is collected AFTER the user passes
    /// verification, so it is linked here rather than at submit. The address must be a valid
    /// 0x-prefixed 40-hex address and becomes the token-allocation destination. A wallet is not
    /// overwritten once set — relinking a profile that already has a wallet returns <c>409</c>.
    /// </remarks>
    /// <param name="request">The wallet address to link.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The wallet was linked.</response>
    /// <response code="400">The wallet address is missing or malformed.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not an Investor.</response>
    /// <response code="404">The caller has no KYC profile yet.</response>
    /// <response code="409">A wallet is already linked to the caller's KYC profile.</response>
    [HttpPatch("wallet")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LinkWallet(LinkWalletRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new LinkKycWalletCommand(request.WalletAddress), ct);
        return ToActionResult(result);
    }

    /// <summary>Sends the SMS code that authorises moving the wallet. Investor only.</summary>
    /// <remarks>
    /// The code goes to the phone registered on the account, never to one supplied in the request.
    /// Call this first, then <c>PATCH /kyc/wallet/change</c> with the code and the new address.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The code was sent.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not an Investor.</response>
    /// <response code="404">The caller has no KYC profile yet.</response>
    /// <response code="409">No wallet is linked yet, or the account has no phone number.</response>
    [HttpPost("wallet/change/request")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestWalletChange(CancellationToken ct)
        => ToActionResult(await Sender.Send(new RequestKycWalletChangeCommand(), ct));

    /// <summary>Moves the allocation address, authorised by an SMS code. Investor only.</summary>
    /// <remarks>
    /// Refused once shares have been issued to the current address or a mint batch is in flight —
    /// a minted position cannot follow the holder, and a batched request names the old address on a
    /// document the exchange already holds. Requests still queued follow the new address.
    /// </remarks>
    /// <param name="request">The new address and the SMS code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The wallet was changed.</response>
    /// <response code="400">The address is malformed, the code is wrong, or it is the same address.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not an Investor.</response>
    /// <response code="404">The caller has no KYC profile yet.</response>
    /// <response code="409">Shares are already issued to the current wallet, or none is linked.</response>
    [HttpPatch("wallet/change")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeWallet(ChangeWalletRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new ConfirmKycWalletChangeCommand(request.WalletAddress, request.Code), ct));

    /// <summary>Returns the current investor's KYC profile state.</summary>
    /// <remarks>
    /// Requires an authenticated user in the <c>Investor</c> role. Reads only the caller's own
    /// KYC profile. Returns <c>404</c> when the caller has never submitted KYC.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The caller's KYC profile status.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not an Investor.</response>
    /// <response code="404">The caller has no KYC profile yet.</response>
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<KycStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetKycStatusQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>A Compliance officer approves or rejects a KYC profile under review.</summary>
    /// <remarks>
    /// Requires an authenticated user in the <c>Compliance</c> role. Approves or rejects the
    /// target profile; a rejection (<c>Approve=false</c>) must include a <c>Reason</c>. Only a
    /// profile in <c>UnderReview</c> can be decided — deciding any other state is a domain rule
    /// violation (<c>400</c>). Returns <c>204 No Content</c> on success.
    /// </remarks>
    /// <param name="id">The KYC profile identifier to review.</param>
    /// <param name="request">The decision: approve, or reject with a reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The decision was applied.</response>
    /// <response code="400">A rejection reason is missing, or the profile is not in a reviewable state.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not a Compliance officer.</response>
    /// <response code="404">No KYC profile exists with the given id.</response>
    [HttpPost("{id:guid}/review")]
    [Authorize(Roles = "Compliance")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Review(Guid id, ReviewKycRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new ReviewKycCommand(id, request.Approve, request.Reason), ct);
        return ToActionResult(result);
    }

    /// <summary>Reads the provider's decision for a stuck profile and applies it. Operator only.</summary>
    /// <remarks>
    /// For the case the webhook did not survive: the verification passed on the provider's side but
    /// our record never heard about it. Asks the provider what it decided and applies that —
    /// approving (with the verified name from the ID document) or rejecting, exactly as the webhook
    /// would have. Idempotent: a profile that is already decided is returned unchanged, and a session
    /// the provider has not decided yet leaves the profile as it is and reports the current status.
    ///
    /// <c>409</c> when the profile has no provider session; <c>502</c> when the provider could not be
    /// asked — an unanswered question is not the same as "still under review", so it is never
    /// reported as one.
    /// </remarks>
    /// <param name="id">The KYC profile to reconcile with its provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The provider was asked; the profile's resulting status is returned.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not an operator.</response>
    /// <response code="404">No KYC profile exists with the given id.</response>
    /// <response code="409">The profile has no provider session to reconcile against.</response>
    /// <response code="502">The provider could not be reached or is misconfigured.</response>
    [HttpPost("{id:guid}/sync")]
    [Authorize(Roles = "Admin,Compliance")]
    [ProducesResponseType<KycStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Sync(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new SyncKycFromProviderCommand(id), ct));
}
