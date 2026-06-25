using System.Collections.Concurrent;
using System.Reflection;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using FluentValidation;
using FluentValidation.Results;

namespace Atria.Infrastructure.Messaging;

/// <summary>
/// Runs all FluentValidation <see cref="IValidator{T}"/> registered for the request.
/// On failure, returns a failed <see cref="Result"/>/<see cref="Result{T}"/> carrying an
/// <see cref="ErrorType.Validation"/> error (preferred over throwing) when TResponse is a
/// Result shape; otherwise throws <see cref="ValidationException"/> for the API to map.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // No validators registered for this request -> straight through.
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = new List<ValidationFailure>();
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(context, ct);
            if (!result.IsValid)
                failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
            return await next();

        // Prefer returning a failed Result; fall back to throwing for non-Result responses.
        var error = BuildError(failures);
        if (TryMakeFailedResult(error, out var failed))
            return failed!;

        throw new ValidationException(failures);
    }

    // Compose a single safe-to-return validation Error (no PII; field names + messages only).
    private static Error BuildError(IReadOnlyList<ValidationFailure> failures)
    {
        var first = failures[0];
        var code = string.IsNullOrEmpty(first.PropertyName) ? "validation" : first.PropertyName;
        var message = string.Join("; ", failures.Select(f => f.ErrorMessage));
        return Error.Validation(code, message);
    }

    // Cache the factory per TResponse so reflection runs at most once per response type.
    private static readonly ConcurrentDictionary<Type, Func<Error, object>?> Factories = new();

    private static bool TryMakeFailedResult(Error error, out TResponse? failed)
    {
        var factory = Factories.GetOrAdd(typeof(TResponse), CreateFactory);
        if (factory is null)
        {
            failed = default;
            return false;
        }

        failed = (TResponse)factory(error);
        return true;
    }

    // Builds a delegate that produces a failed Result / Result<T> for the given Error, or null
    // when TResponse is neither shape (then the behavior throws instead).
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
