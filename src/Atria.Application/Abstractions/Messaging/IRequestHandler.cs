namespace Atria.Application.Abstractions;

/// <summary>
/// Handles exactly one request type. One use case = one handler (no God Service).
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}
