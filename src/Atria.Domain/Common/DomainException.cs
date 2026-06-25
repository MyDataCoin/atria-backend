namespace Atria.Domain.Common;

/// <summary>
/// Thrown when a domain invariant is violated. Mapped to a 4xx ProblemDetails
/// by the API exception middleware (never leaks internals).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

/// <summary>
/// Specialization for illegal State-pattern transitions (e.g. approving a draft).
/// </summary>
public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string message) : base(message) { }
}
