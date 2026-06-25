namespace Atria.Api.Middleware;

/// <summary>
/// Adds baseline HTTP security headers to every response. The Content-Security-Policy is
/// deliberately relaxed enough for the Swagger UI (which needs inline styles/scripts)
/// while still locking the API down for browser callers.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    // Swagger UI bootstraps with inline scripts/styles and an inline data: image, so the
    // CSP must permit 'unsafe-inline' for those directives. APIs are not browser pages,
    // so framing and object/base-uri stay fully locked down.
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = ContentSecurityPolicy;

        // HSTS: only meaningful (and only honoured) over HTTPS. 1 year + subdomains.
        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        return _next(context);
    }
}

/// <summary>Pipeline registration helper.</summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
