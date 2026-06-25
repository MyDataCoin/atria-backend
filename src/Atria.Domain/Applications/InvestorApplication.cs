using Atria.Domain.Applications.States;
using Atria.Domain.Common;

namespace Atria.Domain.Applications;

/// <summary>
/// An investor's application to invest a given amount into a property. Lifecycle is
/// driven by the State pattern (EF-friendly variant): only <see cref="Status"/> is
/// persisted, and the active state is derived from it via <see cref="ApplicationStateFactory"/>.
/// Created exclusively through <c>InvestorApplicationFactory</c>.
/// </summary>
public sealed class InvestorApplication : AggregateRoot
{
    public Guid InvestorId { get; private set; }
    public Guid PropertyId { get; private set; }
    public decimal Amount { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }

    // Private ctor: creation only through the factory. Parameterless overload for EF rehydration.
    private InvestorApplication() { }

    private InvestorApplication(Guid investorId, Guid propertyId, decimal amount)
    {
        Id = Guid.NewGuid();
        InvestorId = investorId;
        PropertyId = propertyId;
        Amount = amount;
        Status = ApplicationStatus.Draft;
    }

    /// <summary>Builds a new draft application. Invoked by <c>InvestorApplicationFactory</c>.</summary>
    internal static InvestorApplication CreateDraft(Guid investorId, Guid propertyId, decimal amount)
        => new(investorId, propertyId, amount);

    // Transition methods delegate to the derived state; the next status is read back from
    // the returned state and persisted on the entity (no mutable _state field for EF).
    public void Submit() => Status = ApplicationStateFactory.Create(Status).Submit(this).Status;
    public void Approve() => Status = ApplicationStateFactory.Create(Status).Approve(this).Status;
    public void Reject(string reason) => Status = ApplicationStateFactory.Create(Status).Reject(this, reason).Status;
    public void MoveToReview() => Status = ApplicationStateFactory.Create(Status).MoveToReview(this).Status;

    /// <summary>State objects raise domain events through this internal hook (same assembly).</summary>
    internal void RaiseDomainEvent(IDomainEvent e) => base.RaiseEvent(e);

    /// <summary>State objects persist the rejection reason on the entity, not on the state.</summary>
    internal void SetRejectionReason(string reason) => RejectionReason = reason;
}
