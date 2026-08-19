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
/// <para>
/// <c>Domain</c> is configurable and empty by default, which leaves the cookie host-only. Setting it
/// to the shared parent domain is what lets one sign-in carry across the public site and the
/// dashboards: they are separate hosts, and a host-only cookie makes each of them a separate session.
/// </para>
/// </remarks>
public static class RefreshTokenCookie
{
    public const string Name = "atria_refresh";

    private const string Path = "/api/v1/auth";

    /// <summary>Writes the refresh token as a cookie scoped to the auth routes.</summary>
    /// <param name="domain">
    /// Shared parent domain (e.g. <c>.atria.kg</c>) so one session spans the site and the dashboards;
    /// null or empty leaves the cookie host-only.
    /// </param>
    public static void Write(
        HttpResponse response, string refreshToken, DateTime expiresAtUtc, string? domain = null)
        => response.Cookies.Append(Name, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.None,
            Path = Path,
            Domain = ScopeFor(response, domain),
            Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)),
            IsEssential = true
        });

    /// <summary>Reads the refresh token from the cookie, or null when absent.</summary>
    public static string? Read(HttpRequest request)
        => request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>Expires the cookie. Attributes must match the ones it was written with.</summary>
    /// <remarks>
    /// The domain is part of that match: deleting without the domain the cookie was issued for leaves
    /// the real one in place and only expires a host-only cookie that was never there.
    /// </remarks>
    public static void Clear(HttpResponse response, string? domain = null)
        => response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.None,
            Path = Path,
            Domain = ScopeFor(response, domain)
        });

    /// <summary>
    /// The configured domain, but only when the host being served actually sits under it; otherwise
    /// null, leaving the cookie host-only.
    /// </summary>
    /// <remarks>
    /// A browser silently discards a cookie whose <c>Domain</c> does not cover the host that sent it,
    /// and a discarded refresh cookie is not a small problem: sign-in appears to succeed and the
    /// session dies at the first refresh. That is what one shared setting would do to every host that
    /// is not the production domain — localhost, a preview deploy, an IP. Matching here means the
    /// setting can be configured once and stay correct everywhere.
    /// </remarks>
    private static string? ScopeFor(HttpResponse response, string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        var request = response.HttpContext.Request;

        // BOTH hosts have to sit under the domain. The serving host is what the browser validates the
        // cookie against, and the origin is the page the cookie has to be useful to; a deployment
        // where those differ — a front proxy that rewrites Host to the API's own name — would
        // otherwise be handed a cookie for a domain the page it came from is not under, and the
        // browser drops it without a word.
        return Covers(domain, request.Host.Host) && Covers(domain, BrowserHost(request))
            ? domain
            : null;
    }

    /// <summary>Does <paramref name="domain"/> (e.g. <c>.atria.kg</c>) cover <paramref name="host"/>?</summary>
    private static bool Covers(string domain, string host)
    {
        var bare = domain.TrimStart('.');

        return host.Equals(bare, StringComparison.OrdinalIgnoreCase)
               || host.EndsWith($".{bare}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The host the BROWSER believes it is talking to, which is the only host a Set-Cookie domain is
    /// checked against.
    /// </summary>
    /// <remarks>
    /// <c>Request.Host</c> is not that host whenever a front nginx proxies <c>/api/</c> and rewrites
    /// the <c>Host</c> header to the API's own name — the arrangement the public site already uses.
    /// The API then sees its own hostname, decides the configured domain covers it, and issues a
    /// cookie the browser silently drops because the page it came from lives somewhere else. The
    /// <c>Origin</c> header carries the host the browser actually used, so it decides here, with
    /// <c>Host</c> as the fallback for callers that send no <c>Origin</c> (curl, the test suite).
    /// </remarks>
    private static string BrowserHost(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();

        return !string.IsNullOrWhiteSpace(origin)
               && Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
            ? parsed.Host
            : request.Host.Host;
    }

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
