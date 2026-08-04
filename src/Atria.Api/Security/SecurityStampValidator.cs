using Atria.Application.Abstractions;
using Atria.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Atria.Api.Security;

/// <summary>
/// Re-checks, on every authenticated request, that the account behind a valid JWT is still in the
/// state the token was issued under.
/// </summary>
/// <remarks>
/// <para>
/// A JWT is a snapshot. Signature and expiry only say the token was minted by us and is not stale
/// yet — they say nothing about whether the account was banned, deactivated or had its password
/// changed thirty seconds ago. Without this check those actions take effect only when the token runs
/// out, which for a <c>Compliance</c> or <c>SuperAdmin</c> holder is fifteen minutes of continued
/// access to burning shares and forced transfers.
/// </para>
/// <para>
/// The mechanism is a stamp rather than a per-token denylist: <see cref="Atria.Domain.Users.User"/>
/// rotates <c>SecurityStamp</c> whenever its security context changes, the stamp travels in the
/// token, and a mismatch fails the request. One indexed primary-key read per request, no extra store
/// to operate, and it covers every already-issued token for that account at once.
/// </para>
/// </remarks>
public static class SecurityStampValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null)
        {
            context.Fail("No principal on the validated token.");
            return;
        }

        var stamp = principal.FindFirst(JwtTokenGenerator.SecurityStampClaimType)?.Value;
        var rawSubject = principal.FindFirst("sub")?.Value;

        // A token minted before the stamp existed, or without a subject, cannot be checked — and an
        // unverifiable token is not a trusted one.
        if (string.IsNullOrEmpty(stamp) || !Guid.TryParse(rawSubject, out var userId))
        {
            context.Fail("The token carries no verifiable account identity.");
            return;
        }

        var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var user = await users.GetByIdAsync(userId, context.HttpContext.RequestAborted);

        if (user is null || user.IsBanned || !user.IsActive || user.DeletedAtUtc is not null)
        {
            context.Fail("The account is no longer permitted to authenticate.");
            return;
        }

        if (!string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal))
        {
            context.Fail("The account's security stamp has changed since this token was issued.");
        }
    }
}
