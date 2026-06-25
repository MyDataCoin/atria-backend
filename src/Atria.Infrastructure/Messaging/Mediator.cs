using System.Collections.Concurrent;
using System.Reflection;
using Atria.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atria.Infrastructure.Messaging;

/// <summary>
/// Thin in-process mediator. Resolves the matching <see cref="IRequestHandler{TRequest,TResponse}"/>
/// and wraps it in the registered <see cref="IPipelineBehavior{TRequest,TResponse}"/> chain
/// (outer-to-inner, in registration order). Reflection is cached per request type.
/// </summary>
public sealed class Mediator : ISender
{
    private readonly IServiceProvider _provider;

    // Cache the compiled invoker per concrete request type so we only reflect once.
    private static readonly ConcurrentDictionary<Type, RequestInvoker> Invokers = new();

    public Mediator(IServiceProvider provider) => _provider = provider;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var invoker = Invokers.GetOrAdd(requestType, static rt => BuildInvoker(rt, typeof(TResponse)));
        return (Task<TResponse>)invoker(_provider, request, ct);
    }

    // Delegate that runs the full pipeline for a given (provider, request, ct).
    private delegate object RequestInvoker(IServiceProvider provider, object request, CancellationToken ct);

    private static RequestInvoker BuildInvoker(Type requestType, Type responseType)
    {
        // Bind the closed generic helper that knows TRequest + TResponse at compile time.
        var helper = typeof(Mediator)
            .GetMethod(nameof(InvokeGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(requestType, responseType);

        return (provider, request, ct) => helper.Invoke(null, [provider, request, ct])!;
    }

    // Strongly-typed pipeline assembly. Behaviors compose outer-to-inner around the handler.
    private static Task<TResponse> InvokeGeneric<TRequest, TResponse>(
        IServiceProvider provider, TRequest request, CancellationToken ct)
        where TRequest : IRequest<TResponse>
    {
        var handler = (IRequestHandler<TRequest, TResponse>)provider
            .GetRequiredService(typeof(IRequestHandler<TRequest, TResponse>));

        // Innermost delegate: the actual handler.
        RequestHandlerDelegate<TResponse> pipeline = () => handler.Handle(request, ct);

        // Wrap behaviors in reverse so the first-registered runs outermost.
        var behaviors = provider
            .GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .ToArray();

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next = pipeline;
            pipeline = () => behavior.Handle(request, next, ct);
        }

        return pipeline();
    }
}
