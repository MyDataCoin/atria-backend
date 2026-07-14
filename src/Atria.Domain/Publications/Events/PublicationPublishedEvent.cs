using Atria.Domain.Common;

namespace Atria.Domain.Publications.Events;

/// <summary>
/// Raised when an admin publishes a publication. Drives the reader notifications: a publication tied
/// to a property notifies that property's holders, a general one (null <see cref="PropertyId"/>)
/// notifies every investor.
/// </summary>
public sealed record PublicationPublishedEvent(
    Guid PublicationId,
    Guid? PropertyId,
    string Title) : DomainEventBase, IExplicitlyAudited;
