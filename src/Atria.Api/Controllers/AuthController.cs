using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Auth.Commands;
using Atria.Application.Auth.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Phone-first authentication (Kyrgyzstan +996): OTP registration/sign-in and token refresh.</summary>
/// <remarks>
/// There is NO email/password login — accounts are created and authenticated purely via a
/// phone OTP: <c>register/phone/request-otp</c> → <c>register/phone/verify-otp</c>, then
/// <c>refresh</c> rotates the token pair.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[AllowAnonymous]
public sealed class AuthController : ApiControllerBase
{
    public AuthController(ISender sender) : base(sender) { }

    /// <summary>Logs an admin in with a static username/password and returns a token pair.</summary>
    /// <remarks>
    /// Anonymous endpoint, separate from the phone flow (admins have no SMS login). Credentials are
    /// the static <c>Admin__Username</c> / <c>Admin__Password</c> from server configuration and are
    /// checked in constant time; on success an <c>Admin</c> access token + refresh token are issued
    /// for the configured admin user. The feature is disabled (always <c>401</c>) when no admin
    /// password is configured. Invalid credentials return <c>401</c> without revealing which field failed.
    /// </remarks>
    /// <param name="request">The admin username and static password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Credentials accepted; access and refresh tokens returned.</response>
    /// <response code="400">The request failed validation (missing username or password).</response>
    /// <response code="401">Admin login is disabled or the credentials are invalid.</response>
    [HttpPost("admin/login")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AdminLogin(AdminLoginRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new AdminLoginCommand(request.Username, request.Password), ct);
        return ToActionResult(result);
    }

    /// <summary>Logs a realtor in with a static username/password and returns a token pair.</summary>
    /// <remarks>
    /// Anonymous endpoint, separate from the phone flow (realtors have no SMS login). Credentials are
    /// the static <c>Realtor__Username</c> / <c>Realtor__Password</c> from server configuration and are
    /// checked in constant time; on success a <c>Realtor</c> access token + refresh token are issued
    /// for the configured realtor user. The feature is disabled (always <c>401</c>) when no realtor
    /// password is configured. Invalid credentials return <c>401</c> without revealing which field failed.
    /// </remarks>
    /// <param name="request">The realtor username and static password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Credentials accepted; access and refresh tokens returned.</response>
    /// <response code="400">The request failed validation (missing username or password).</response>
    /// <response code="401">Realtor login is disabled or the credentials are invalid.</response>
    [HttpPost("realtor/login")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RealtorLogin(RealtorLoginRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new RealtorLoginCommand(request.Username, request.Password), ct);
        return ToActionResult(result);
    }

    /// <summary>Rotates a refresh token into a fresh access + refresh pair.</summary>
    /// <remarks>
    /// Anonymous endpoint. Exchanges a valid refresh token for a brand-new access + refresh
    /// pair and revokes the presented token (rotation). Reuse detection: presenting a token
    /// that was already revoked is treated as a leak and revokes the user's entire session.
    /// Missing, expired or already-rotated tokens return <c>401</c>.
    /// </remarks>
    /// <param name="request">The refresh token previously issued by an auth endpoint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A fresh access and refresh token pair was issued.</response>
    /// <response code="400">The request failed validation (missing refresh token).</response>
    /// <response code="401">The refresh token is invalid, expired, or has been rotated/revoked.</response>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new RefreshTokenCommand(request.RefreshToken), ct);
        return ToActionResult(result);
    }

    /// <summary>Requests a one-time SMS code for phone registration/login.</summary>
    /// <remarks>
    /// Anonymous endpoint and step 1 of the phone-first Kyrgyzstan flow. The phone must be a
    /// Kyrgyz number in <c>+996XXXXXXXXX</c> form (e.g. <c>+996700123456</c>); it is normalized
    /// server-side. The caller's IP is captured here for rate limiting and the number is capped
    /// to a few codes per rolling hour (<c>409</c> when exceeded). Returns <c>204 No Content</c>
    /// on success. Dev note: when an OTP dev code is configured, no SMS is sent and a fixed code
    /// is used so the flow can be tested without an SMS provider. Follow up with
    /// <c>register/phone/verify-otp</c>.
    /// </remarks>
    /// <param name="request">The destination phone number in <c>+996XXXXXXXXX</c> format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">A code was issued (SMS sent, or a fixed dev code used in development).</response>
    /// <response code="400">The phone number failed validation.</response>
    /// <response code="409">Too many OTP requests for this number; try again later.</response>
    /// <response code="502">The SMS gateway rejected the request or was unreachable (detail carries the smspro status).</response>
    [HttpPost("register/phone/request-otp")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> RequestPhoneOtp(RequestOtpRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await Sender.Send(new RequestPhoneOtpCommand(request.Phone, ip), ct);
        return ToActionResult(result);
    }

    /// <summary>Verifies the SMS code, creating the account on first use, and returns a token pair.</summary>
    /// <remarks>
    /// Anonymous endpoint and step 2 of the phone-first Kyrgyzstan flow. Supply the same
    /// <c>+996XXXXXXXXX</c> number used for <c>request-otp</c> plus the received code. On the
    /// first successful verification an <c>Investor</c> account is created and marked
    /// phone-verified; on subsequent calls the existing account is used. Codes are single-use
    /// and expire; too many wrong attempts locks the code (<c>409</c>). Dev note: in development
    /// the code is a fixed configured value (no SMS sent).
    /// </remarks>
    /// <param name="request">The phone number (<c>+996XXXXXXXXX</c>) and the one-time code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Code verified; access and refresh tokens returned.</response>
    /// <response code="400">The request failed validation, or the code is invalid/expired.</response>
    /// <response code="409">The code is locked after too many incorrect attempts; request a new one.</response>
    [HttpPost("register/phone/verify-otp")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VerifyPhoneOtp(VerifyOtpRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new VerifyPhoneOtpCommand(request.Phone, request.Code), ct);
        return ToActionResult(result);
    }
}
