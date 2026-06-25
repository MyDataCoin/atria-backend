using System.Text.Json;
using Atria.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atria.Api.Middleware;

/// <summary>
/// Last-resort guard for UNHANDLED exceptions. Maps domain invariant violations to 400
/// and everything else to 500, and returns a SANITIZED <see cref="ProblemDetails"/>
/// (no stack traces / internal messages) carrying the request correlation id. The full
/// exception detail is logged internally against the same correlation id.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.GetCorrelationId();

        var (status, title) = exception switch
        {
            // InvalidStateTransitionException : DomainException, so it is covered here too.
            DomainException => (StatusCodes.Status400BadRequest, "A domain rule was violated."),
            // Concurrent/duplicate writes (e.g. a redelivered webhook racing the original):
            // surface 409 so the caller/provider can safely retry rather than seeing a 500.
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The resource was modified concurrently. Please retry."),
            DbUpdateException => (StatusCodes.Status409Conflict, "The operation conflicts with existing data."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        // Full detail stays INTERNAL only (never returned to the caller).
        _logger.Log(
            status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
            exception,
            "Unhandled exception. CorrelationId={CorrelationId} Status={Status} Path={Path}",
            correlationId, status, context.Request.Path.Value);

        if (context.Response.HasStarted)
        {
            // Cannot rewrite a response whose body is already flushing.
            _logger.LogWarning(
                "Response already started; could not write ProblemDetails. CorrelationId={CorrelationId}",
                correlationId);
            return;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // Domain messages are safe to surface (invariant text, no internals);
            // generic faults expose nothing.
            Detail = exception is DomainException ? exception.Message : null,
            Instance = context.Request.Path,
        };
        problem.Extensions["correlationId"] = correlationId;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}

/// <summary>Pipeline registration helper.</summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}
