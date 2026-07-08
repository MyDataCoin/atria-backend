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
        if (ResultFactory.TryMakeFailure<TResponse>(error, out var failed))
            return failed;

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
}
