using Atria.Application.Abstractions;
using Atria.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Messaging;

/// <summary>
/// Catches <see cref="DbUpdateConcurrencyException"/> raised by a handler's SaveChanges (the
/// loaded row was changed or removed by another request in flight) and reports it as a 409
/// Conflict Result instead of an unhandled 500, when TResponse is a Result shape. Registered
/// around <see cref="LoggingBehavior{TRequest,TResponse}"/> so the raw exception is still logged
/// before being converted here.
/// </summary>
public sealed class ConcurrencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (DbUpdateConcurrencyException)
        {
            var error = Error.Conflict(
                "concurrency.conflict",
                "This record was changed or removed by another request. Please retry.");

            if (ResultFactory.TryMakeFailure<TResponse>(error, out var failed))
                return failed;

            throw;
        }
    }
}
