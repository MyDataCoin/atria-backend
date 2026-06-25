namespace Atria.Application.Abstractions;

/// <summary>Marker for any request (command or query).</summary>
public interface IBaseRequest;

/// <summary>A request that produces a <typeparamref name="TResponse"/>.</summary>
public interface IRequest<out TResponse> : IBaseRequest;
