using Atria.Domain.Common;
using Atria.Domain.Kyc.States;

namespace Atria.Domain.Kyc;

/// <summary>
/// Investor KYC aggregate. Lifecycle is driven by the State pattern (EF-friendly
/// variant): only <see cref="Status"/> is persisted and the current state is
/// derived from it via <see cref="KycStateFactory"/>. FullName and DocumentNumber
/// are PII and are encrypted at rest by the infrastructure value converters.
/// </summary>
public sealed class KycProfile : AggregateRoot
{
    public Guid UserId { get; private set; }
    public KycStatus Status { get; private set; }
    public KycProviderType Provider { get; private set; }
    public string? FullName { get; private set; }        // PII — encrypted at rest
    public string? DocumentNumber { get; private set; }  // PII — encrypted at rest
    public string? Nationality { get; private set; }
    public string? WalletAddress { get; private set; }
    public string? ProviderSessionId { get; private set; }
    public string? VerificationUrl { get; private set; }   // hosted provider URL, for resuming an unfinished flow
    public string? RejectionReason { get; private set; }

    // private ctor: creation only through the static factory
    private KycProfile() { }

    /// <summary>Creates a new KYC profile in the Pending state for a user.</summary>
    public static KycProfile Create(Guid userId)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = KycStatus.Pending
        };

    /// <summary>
    /// Records provider/session and PII details, then transitions
    /// Pending -> UnderReview (raises <c>KycSubmittedEvent</c>).
    /// </summary>
    public void Submit(KycProviderType provider, string sessionId, string? verificationUrl,
        string? walletAddress, string? fullName, string? documentNumber, string? nationality)
    {
        Provider = provider;
        ProviderSessionId = sessionId;
        VerificationUrl = verificationUrl;
        WalletAddress = walletAddress;
        FullName = fullName;
        DocumentNumber = documentNumber;
        Nationality = nationality;
        Status = KycStateFactory.Create(Status).Submit(this).Status;
    }

    /// <summary>
    /// Links the investor's crypto wallet after verification (the token-allocation address).
    /// Format is validated at the application boundary; the already-linked conflict is handled
    /// by the caller. Empty input is rejected here as a last-resort invariant.
    /// </summary>
    public void LinkWallet(string walletAddress)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new DomainException("Wallet address is required.");

        WalletAddress = walletAddress;
    }

    /// <summary>Transitions UnderReview -> Approved (raises <c>KycApprovedEvent</c>).</summary>
    public void Approve()
        => Status = KycStateFactory.Create(Status).Approve(this).Status;

    /// <summary>Stores the reason and transitions to Rejected (raises <c>KycRejectedEvent</c>).</summary>
    public void Reject(string reason)
    {
        RejectionReason = reason;
        Status = KycStateFactory.Create(Status).Reject(this, reason).Status;
    }

    // called by state objects (same assembly) to record domain events
    internal void RaiseDomainEvent(IDomainEvent e) => base.RaiseEvent(e);
}
