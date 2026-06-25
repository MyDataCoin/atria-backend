using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Auth.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Authentication: email/password + phone-OTP registration and token refresh.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[AllowAnonymous]
public sealed class AuthController : ApiControllerBase
{
    public AuthController(ISender sender) : base(sender) { }

    /// <summary>Registers a new email/password account and returns a token pair.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new RegisterCommand(request.Email, request.Password, request.FirstName, request.LastName), ct);
        return ToActionResult(result);
    }

    /// <summary>Authenticates an email/password account and returns a token pair.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new LoginCommand(request.Email, request.Password), ct);
        return ToActionResult(result);
    }

    /// <summary>Rotates a refresh token into a fresh access + refresh pair.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new RefreshTokenCommand(request.RefreshToken), ct);
        return ToActionResult(result);
    }

    /// <summary>Requests a one-time SMS code for phone registration; the client IP is captured here for rate limiting.</summary>
    [HttpPost("register/phone/request-otp")]
    public async Task<IActionResult> RequestPhoneOtp(RequestOtpRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await Sender.Send(new RequestPhoneOtpCommand(request.Phone, ip), ct);
        return ToActionResult(result);
    }

    /// <summary>Verifies the SMS code, creating the account on first use, and returns a token pair.</summary>
    [HttpPost("register/phone/verify-otp")]
    public async Task<IActionResult> VerifyPhoneOtp(VerifyOtpRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new VerifyPhoneOtpCommand(request.Phone, request.Code), ct);
        return ToActionResult(result);
    }
}
