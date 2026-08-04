using Microsoft.Net.Http.Headers;

namespace Atria.Api.Security;

/// <summary>
/// Carries the refresh token in a cookie the browser cannot read.
/// </summary>
/// <remarks>
/// <para>
/// Both dashboards used to keep the refresh token in <c>localStorage</c>, where any script running
/// on the origin can read it — one XSS, or one compromised package in the bundle, and the attacker
/// holds a thirty-day credential for a SuperAdmin session. Rotation and reuse detection do not help
/// against that: the attacker has a valid token and can rotate it themselves.
/// </para>
/// <para>
/// <c>HttpOnly</c> puts it out of JavaScript's reach. <c>SameSite=None</c> is required because the
/// dashboards and the API are different hosts, and that is safe here rather than a CSRF hole: the
/// cookie alone authorises exactly one thing — exchanging it at <c>/auth/refresh</c> — and the reply
/// is a cross-origin response the attacking page cannot read. Every other endpoint needs the bearer
/// token, which is never in a cookie and so never travels automatically.
/// </para>
/// <para>
/// <c>Path</c> confines the cookie to the auth routes, so it is not attached to ordinary API calls.
/// </para>
/// </remarks>
public static class RefreshTokenCookie
{
    public const string Name = "atria_refresh";

    private const string Path = "/api/v1/auth";

    /// <summary>Writes the refresh token as a cookie scoped to the auth routes.</summary>
    public static void Write(HttpResponse response, string refreshToken, DateTime expiresAtUtc)
        => response.Cookies.Append(Name, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.None,
            Path = Path,
            Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)),
            IsEssential = true
        });

    /// <summary>Reads the refresh token from the cookie, or null when absent.</summary>
    public static string? Read(HttpRequest request)
        => request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>Expires the cookie. Attributes must match the ones it was written with.</summary>
    public static void Clear(HttpResponse response)
        => response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.None,
            Path = Path
        });

    /// <summary>
    /// Marks a response as varying by cookie so a shared cache cannot serve one caller's token pair
    /// to another.
    /// </summary>
    public static void MarkUncacheable(HttpResponse response)
    {
        response.Headers[HeaderNames.CacheControl] = "no-store";
        response.Headers[HeaderNames.Vary] = HeaderNames.Cookie;
    }
}
