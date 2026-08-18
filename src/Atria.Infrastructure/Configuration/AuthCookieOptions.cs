namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Scope of the refresh-token cookie.
/// </summary>
/// <remarks>
/// Without a domain the browser makes the cookie host-only, tied to whichever host served the
/// response. That is the safest default and the right one for local development, but it means each
/// frontend host gets its own separate session: a person signed in on the public site arrives at the
/// investor dashboard on a different subdomain as a stranger, because the cookie that proves the
/// session was never sent there.
/// </remarks>
public sealed class AuthCookieOptions
{
    public const string SectionName = "AuthCookie";

    /// <summary>
    /// Domain the refresh cookie is issued for, e.g. <c>.atria.kg</c> to share one session across the
    /// public site, the investor dashboard and the admin dashboard. Empty leaves the cookie host-only.
    /// </summary>
    /// <remarks>
    /// A domain-wide cookie is offered to every subdomain, so this is only safe while every host under
    /// the domain is ours. It buys exactly one capability — exchanging the cookie at
    /// <c>/api/v1/auth/refresh</c> — because the path confines it there and <c>HttpOnly</c> keeps it out
    /// of reach of any script that might run on those hosts.
    /// </remarks>
    public string Domain { get; init; } = string.Empty;
}
