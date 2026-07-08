using System.Collections.Concurrent;
using System.Reflection;
using Atria.Application.Common;

namespace Atria.Infrastructure.Messaging;

/// <summary>
/// Builds a failed <see cref="Result"/>/<see cref="Result{T}"/> for a given response type via
/// reflection, so pipeline behaviors can short-circuit generically without knowing TResponse's
/// shape. Shared by behaviors that need to turn a caught condition into a Result instead of
/// letting it throw (validation failures, concurrency conflicts, ...).
/// </summary>
internal static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>?> Factories = new();

    public static bool TryMakeFailure<TResponse>(Error error, out TResponse failed)
    {
        var factory = Factories.GetOrAdd(typeof(TResponse), CreateFactory);
        if (factory is null)
        {
            failed = default!;
            return false;
        }

        failed = (TResponse)factory(error);
        return true;
    }

    // Builds a delegate that produces a failed Result / Result<T> for the given Error, or null
    // when TResponse is neither shape (callers should throw instead).
    private static Func<Error, object>? CreateFactory(Type responseType)
    {
        if (responseType == typeof(Result))
            return error => Result.Failure(error);

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = responseType.GetGenericArguments()[0];

            // Result.Failure<TValue>(Error) -> Result<TValue>
            var method = typeof(Result)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m is { Name: nameof(Result.Failure), IsGenericMethodDefinition: true }
                            && m.GetParameters().Length == 1)
                .MakeGenericMethod(valueType);

            return error => method.Invoke(null, [error])!;
        }

        return null;
    }
}
