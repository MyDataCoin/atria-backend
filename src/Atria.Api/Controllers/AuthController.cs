using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Api.Security;
using Atria.Application.Abstractions;
using Atria.Application.Auth.Commands;
using Atria.Application.Auth.Dtos;
using Atria.Application.Auth.Queries;
using Atria.Application.Common;
using Atria.Application.Users.Dtos;
using Atria.Application.Users.Queries;
using Atria.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    private readonly string? _cookieDomain;
    private readonly BrowserOrigins _browserOrigins;

    public AuthController(
        ISender sender, IOptions<AuthCookieOptions> cookieOptions, BrowserOrigins browserOrigins)
        : base(sender)
    {
        _cookieDomain = cookieOptions.Value.Domain;
        _browserOrigins = browserOrigins;
    }

    /// <summary>
    /// Returns the token pair and, on success, ALSO plants the refresh token in an HttpOnly cookie.
    /// </summary>
    /// <remarks>
    /// The body keeps carrying the refresh token so non-browser callers (the integration suite,
    /// scripts, any future mobile client) are unaffected. Browsers are expected to ignore it and let
    /// the cookie do the work — see <see cref="RefreshTokenCookie"/> for why leaving a thirty-day
    /// credential in localStorage was the problem worth solving.
    /// </remarks>
    private IActionResult TokensWithRefreshCookie(Result<AuthTokensDto> result)
    {
        if (result.IsSuccess)
        {
            RefreshTokenCookie.MarkUncacheable(Response);
            RefreshTokenCookie.Write(
                Response, result.Value.RefreshToken, result.Value.RefreshExpiresAtUtc, _cookieDomain);
        }

        return ToActionResult(result);
    }

    /// <summary>Returns the authenticated caller's own account: role, login, name, password state.</summary>
    /// <remarks>
    /// Any authenticated role. The panels call this right after a sign-in: the access token carries
    /// no name (it is held client-side, so every claim in it is published), and the
    /// <c>mustChangePassword</c> flag here is what tells the client to put up the forced
    /// password-change dialog before letting the account work.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The caller's account row.</response>
    /// <response code="401">The request is not authenticated.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetCurrentUserQuery(), ct));

    /// <summary>Changes the signed-in account's own password and returns a fresh token pair.</summary>
    /// <remarks>
    /// Credential accounts (admin/realtor/super admin) only. This is how an account clears the
    /// forced change it is under after a super admin creates it or resets its password. The new
    /// password must be at least six characters with an upper-case letter, a lower-case letter, a
    /// digit and a special character, and must differ from the current one (<c>409</c>).
    /// <para>
    /// A fresh pair comes back because setting a password rotates the account's security stamp:
    /// every token issued under the old password — the caller's included — stops validating, and
    /// refresh tokens from before the change are revoked, so a session opened with the temporary
    /// password cannot outlive it.
    /// </para>
    /// </remarks>
    /// <param name="request">The current password and its replacement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The password was changed; new tokens returned.</response>
    /// <response code="400">The new password failed the strength policy.</response>
    /// <response code="401">Not authenticated, or the current password is wrong.</response>
    /// <response code="409">The account has no password, or the new password repeats the current one.</response>
    [HttpPost("password/change")]
    [Authorize]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new ChangeOwnPasswordCommand(request.CurrentPassword, request.NewPassword), ct);
        return TokensWithRefreshCookie(result);
    }

    /// <summary>Logs an admin (or super admin) in with a username/password and returns a token pair.</summary>
    /// <remarks>
    /// Anonymous endpoint, separate from the phone flow (admins have no SMS login). The account is
    /// looked up by username in the database and the password is verified against its stored hash; the
    /// issued token's role (<c>Admin</c> or <c>SuperAdmin</c>) comes from the row, so this one endpoint
    /// serves both. Accounts are ordinary <c>users</c> rows — no configuration. A missing, wrong, or
    /// banned account returns <c>401</c> without revealing which field failed.
    /// </remarks>
    /// <param name="request">The account username and password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Credentials accepted; access and refresh tokens returned.</response>
    /// <response code="400">The request failed validation (missing username or password).</response>
    /// <response code="401">The credentials are invalid or the account is banned.</response>
    [HttpPost("admin/login")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AdminLogin(AdminLoginRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new AdminLoginCommand(request.Username, request.Password), ct);
        return TokensWithRefreshCookie(result);
    }

    /// <summary>Logs a realtor in with a username/password and returns a token pair.</summary>
    /// <remarks>
    /// Anonymous endpoint, separate from the phone flow (realtors have no SMS login). The account is
    /// looked up by username in the database and the password is verified against its stored hash; a
    /// <c>Realtor</c> token is issued. Accounts are ordinary <c>users</c> rows — no configuration. A
    /// missing, wrong, or banned account returns <c>401</c> without revealing which field failed.
    /// </remarks>
    /// <param name="request">The realtor username and password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Credentials accepted; access and refresh tokens returned.</response>
    /// <response code="400">The request failed validation (missing username or password).</response>
    /// <response code="401">The credentials are invalid or the account is banned.</response>
    [HttpPost("realtor/login")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RealtorLogin(RealtorLoginRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new RealtorLoginCommand(request.Username, request.Password), ct);
        return TokensWithRefreshCookie(result);
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
    public async Task<IActionResult> Refresh(RefreshTokenRequest? request, CancellationToken ct)
    {
        // The cookie wins when present: a browser that stopped storing the token sends an empty
        // body, and a stale copy in a body should never override the one the browser holds.
        var presented = RefreshTokenCookie.Read(Request) ?? request?.RefreshToken;

        var result = await Sender.Send(new RefreshTokenCommand(presented ?? string.Empty), ct);

        // A refused refresh means the cookie cannot authenticate anything any more; leaving it in
        // place only guarantees the next attempt fails the same way.
        if (result.IsFailure)
            RefreshTokenCookie.Clear(Response, _cookieDomain);

        return TokensWithRefreshCookie(result);
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

    /// <summary>Says whether a phone number already has an account, before any code is sent.</summary>
    /// <remarks>
    /// Anonymous endpoint. The public site asks this the moment a number is typed, so «Войти» with a
    /// number nobody registered says so at once instead of after the code, and «Регистрация» with a
    /// number that already exists sends the person to sign in.
    /// <para>
    /// This DOES answer "do you have an account for this number?" to anyone, which is a trade made
    /// deliberately for that flow. It is rate-limited per address like the other auth routes, answers
    /// with a single boolean and nothing else, and the same check is repeated at
    /// <c>verify-otp</c> (where it is gated by the code) so the guarantee does not rest on this call.
    /// </para>
    /// </remarks>
    /// <param name="request">The phone number in <c>+996XXXXXXXXX</c> form.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Whether the number is registered.</response>
    /// <response code="400">The phone number failed validation.</response>
    /// <response code="429">Too many checks from this address.</response>
    [HttpPost("phone/status")]
    [ProducesResponseType<PhoneRegistrationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> PhoneStatus(RequestOtpRequest request, CancellationToken ct)
    {
        RefreshTokenCookie.MarkUncacheable(Response);
        return ToActionResult(await Sender.Send(new GetPhoneRegistrationQuery(request.Phone), ct));
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
    /// <response code="404"><c>intent=login</c> and this number has no account (<c>auth.phone_not_registered</c>).</response>
    /// <response code="409">
    /// The code is locked after too many incorrect attempts; or <c>intent=register</c> and the number
    /// already has an account (<c>auth.phone_already_registered</c>).
    /// </response>
    [HttpPost("register/phone/verify-otp")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VerifyPhoneOtp(VerifyOtpRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new VerifyPhoneOtpCommand(request.Phone, request.Code, IntentFrom(request.Intent)), ct);
        return TokensWithRefreshCookie(result);
    }

    /// <summary>
    /// Reads the caller's intent, defaulting to the create-or-sign-in behaviour.
    /// </summary>
    /// <remarks>
    /// A value we do not recognise is treated as "no preference" rather than a 400: the field is an
    /// addition, and an older or third-party client that sends something else should keep working
    /// exactly as it did.
    /// </remarks>
    private static PhoneAuthIntent IntentFrom(string? intent) => intent?.Trim().ToLowerInvariant() switch
    {
        "login" => PhoneAuthIntent.Login,
        "register" => PhoneAuthIntent.Register,
        _ => PhoneAuthIntent.Any
    };

    /// <summary>Ends the session: revokes the refresh token and clears its cookie.</summary>
    /// <remarks>
    /// Anonymous, because a session that has already lost its access token still has to be endable.
    /// The refresh token is taken from the cookie when present, else from the body. Always answers
    /// <c>204</c>: whether the token existed is not something an unauthenticated caller needs told.
    /// <para>
    /// The cookie is <c>SameSite=None</c> (the site and the apps live on different subdomains), so
    /// the browser attaches it to a cross-site POST as well — an attacker's page could otherwise
    /// force any visitor, administrators included, out of their session by submitting a form here.
    /// A request that carries an <c>Origin</c> we do not serve is therefore refused before the
    /// token is revoked; non-browser callers send no <c>Origin</c> and are unaffected.
    /// </para>
    /// </remarks>
    /// <param name="request">Optional body carrying the refresh token (non-browser clients).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The session was ended.</response>
    /// <response code="403">The request came from an origin this API does not serve.</response>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Logout(RefreshTokenRequest? request, CancellationToken ct)
    {
        if (!_browserOrigins.IsAllowed(Request))
            return Forbid();

        var presented = RefreshTokenCookie.Read(Request) ?? request?.RefreshToken;

        var result = await Sender.Send(new LogoutCommand(presented ?? string.Empty), ct);

        RefreshTokenCookie.MarkUncacheable(Response);
        RefreshTokenCookie.Clear(Response, _cookieDomain);

        return ToActionResult(result);
    }
}
