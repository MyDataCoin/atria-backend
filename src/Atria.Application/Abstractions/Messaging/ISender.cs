namespace Atria.Application.Abstractions;

/// <summary>
/// Thin in-process mediator. Controllers depend on this only. The implementation
/// (Infrastructure) resolves the matching handler and runs the pipeline behaviors.
/// </summary>
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
