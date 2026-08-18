namespace Atria.Api.Security;

/// <summary>
/// The exact browser origins the API answers cross-origin (the same list CORS is built from).
/// Registered as a singleton so state-changing endpoints can check the <c>Origin</c> header
/// themselves instead of re-reading configuration and drifting from the CORS policy.
/// </summary>
/// <remarks>
/// CORS alone does not stop a forged request: the browser sends it, applies the policy to the
/// RESPONSE, and by then the server has already acted. Endpoints that change state on a cookie
/// alone must therefore look at <c>Origin</c> before doing the work — see <see cref="IsAllowed"/>.
/// </remarks>
public sealed class BrowserOrigins
{
    private readonly HashSet<string> _origins;

    public BrowserOrigins(IEnumerable<string> origins)
        => _origins = new HashSet<string>(origins, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the request did not come from a browser page, or came from one we trust.
    /// </summary>
    /// <remarks>
    /// A missing <c>Origin</c> is allowed on purpose: browsers attach it to every cross-site POST,
    /// so its absence means a non-browser caller (script, integration suite, mobile client) that
    /// no page can forge a request on behalf of. A PRESENT but unlisted origin is the forgery case.
    /// </remarks>
    public bool IsAllowed(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || _origins.Contains(origin);
    }
}
