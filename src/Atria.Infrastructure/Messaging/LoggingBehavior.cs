using System.Diagnostics;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Messaging;

/// <summary>
/// Logs the request type name, outcome and elapsed time. Logs ONLY the type name and the
/// safe <see cref="Error.Code"/>/<see cref="ErrorType"/> — never request contents, PII,
/// secrets, OTP codes or tokens.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("Handling {RequestName}", requestName);

        try
        {
            var response = await next();
            sw.Stop();

            // For Result responses, log the outcome with the safe error code/type only.
            if (response is Result result && result.IsFailure)
            {
                _logger.LogWarning(
                    "{RequestName} failed in {ElapsedMs}ms: {ErrorType}/{ErrorCode}",
                    requestName, sw.ElapsedMilliseconds, result.Error.Type, result.Error.Code);
            }
            else
            {
                _logger.LogInformation(
                    "Handled {RequestName} in {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Log the exception type only here; the API exception middleware records the rest.
            _logger.LogError(
                ex, "{RequestName} threw {ExceptionType} after {ElapsedMs}ms",
                requestName, ex.GetType().Name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
