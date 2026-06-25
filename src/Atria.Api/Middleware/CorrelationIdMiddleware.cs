using Serilog.Context;

namespace Atria.Api.Middleware;

/// <summary>
/// Reads (or creates) an X-Correlation-ID for every request, stores it in
/// <see cref="HttpContext.Items"/>, echoes it on the response, and pushes it onto the
/// Serilog <see cref="LogContext"/> so every log line of this request carries it.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[ItemKey] = correlationId;

        // Ensure the header is present on the response even if the body has started.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Enrich every Serilog event produced while this request is in flight.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var provided)
            && !string.IsNullOrWhiteSpace(provided))
        {
            return provided.ToString();
        }

        return Guid.NewGuid().ToString("D");
    }
}

/// <summary>Pipeline registration helper.</summary>
public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();

    /// <summary>Reads the correlation id resolved for the current request (or a fallback).</summary>
    public static string GetCorrelationId(this HttpContext context)
        => context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value)
           && value is string id
            ? id
            : context.TraceIdentifier;
}
