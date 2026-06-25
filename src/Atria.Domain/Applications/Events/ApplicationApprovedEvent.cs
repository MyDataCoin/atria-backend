using Atria.Domain.Common;

namespace Atria.Domain.Applications.Events;

/// <summary>Raised when an application is approved — triggers Investment creation (cross-module).</summary>
public sealed record ApplicationApprovedEvent(
    Guid ApplicationId,
    Guid PropertyId,
    Guid InvestorId,
    decimal Amount) : DomainEventBase;
