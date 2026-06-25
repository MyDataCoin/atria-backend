using Atria.Domain.Common;

namespace Atria.Domain.Applications.Events;

/// <summary>Raised when an investor submits a draft application for review.</summary>
public sealed record ApplicationSubmittedEvent(
    Guid ApplicationId,
    Guid InvestorId,
    Guid PropertyId,
    decimal Amount) : DomainEventBase;
