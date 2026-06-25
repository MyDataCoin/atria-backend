using System.Collections.Concurrent;
using System.Reflection;
using Atria.Application.Abstractions;
using Atria.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Atria.Infrastructure.Events;

/// <summary>
/// Resolves every <see cref="IDomainEventHandler{TEvent}"/> registered for the runtime event
/// type and invokes its <c>HandleAsync</c> via reflection. Used by the outbox dispatcher; the
/// handler runs against the same scoped <see cref="IServiceProvider"/> (DbContext, repos).
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _provider;

    // Cache reflection lookups (handler interface type + HandleAsync MethodInfo) per event type.
    private static readonly ConcurrentDictionary<Type, HandlerMeta> MetaCache = new();

    public DomainEventDispatcher(IServiceProvider provider) => _provider = provider;

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var domainEvent in events)
            await DispatchAsync(domainEvent, ct);
    }

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
        var meta = MetaCache.GetOrAdd(eventType, static et => new HandlerMeta(et));

        // Resolve all registered handlers for the concrete event type.
        var handlers = (IEnumerable<object?>)_provider.GetServices(meta.HandlerInterfaceType);

        foreach (var handler in handlers)
        {
            if (handler is null)
                continue;

            // HandleAsync(TEvent, CancellationToken) -> Task
            var task = (Task)meta.HandleMethod.Invoke(handler, [domainEvent, ct])!;
            await task;
        }
    }

    // Precomputed handler interface + method for a given event type.
    private sealed class HandlerMeta
    {
        public Type HandlerInterfaceType { get; }
        public MethodInfo HandleMethod { get; }

        public HandlerMeta(Type eventType)
        {
            HandlerInterfaceType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            HandleMethod = HandlerInterfaceType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
        }
    }
}
