namespace Atria.Domain.Common;

/// <summary>
/// Marks a domain event whose audit entry is written explicitly inside the command that raised it
/// (with the real actor, a composed summary and a severity, in the same transaction). The universal
/// background audit handler skips these so the journal does not show a second, anonymous duplicate.
/// </summary>
public interface IExplicitlyAudited;
