namespace Atria.Domain.Kyc.States;

/// <summary>
/// State pattern (EF-friendly variant): each state is a stateless singleton that
/// knows its <see cref="Status"/> and which transitions are legal. Transition
/// methods raise the relevant domain event on the entity and return the next
/// state; illegal transitions throw <c>InvalidStateTransitionException</c>.
/// The status enum is the only thing persisted; the state is derived from it via
/// <see cref="KycStateFactory"/>.
/// </summary>
public interface IKycState
{
    KycStatus Status { get; }
    IKycState Submit(KycProfile profile);
    IKycState Approve(KycProfile profile);
    IKycState Reject(KycProfile profile, string reason);
}
