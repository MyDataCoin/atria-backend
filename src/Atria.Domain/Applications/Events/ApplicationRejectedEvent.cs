using Atria.Domain.Common;

namespace Atria.Domain.Applications.Events;

/// <summary>Raised when an application is rejected, carrying the rejection reason.</summary>
public sealed record ApplicationRejectedEvent(
    Guid ApplicationId,
    Guid InvestorId,
    string Reason) : DomainEventBase;
