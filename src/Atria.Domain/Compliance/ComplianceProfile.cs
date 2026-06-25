using Atria.Domain.Common;
using Atria.Domain.Compliance.Events;

namespace Atria.Domain.Compliance;

/// <summary>
/// Off-chain compliance record for an investor: holds the DID, attestation payload
/// and allowlist/revocation flags. Identity data lives here (never on chain); only
/// attestation roots are anchored externally (see Infrastructure).
/// </summary>
public sealed class ComplianceProfile : AggregateRoot
{
    public Guid InvestorId { get; private set; }
    public string? Did { get; private set; }
    public string? WalletAddress { get; private set; }
    public bool IsAllowlisted { get; private set; }
    public bool IsRevoked { get; private set; }
    public string? AttestationsJson { get; private set; }
    public string? RevocationReason { get; private set; }

    // EF / factory only.
    private ComplianceProfile() { }

    /// <summary>Creates a profile for an investor; validates the wallet address when supplied.</summary>
    public static ComplianceProfile Create(Guid investorId, string? walletAddress)
    {
        if (walletAddress is not null && !Compliance.WalletAddress.IsValid(walletAddress))
            throw new DomainException("Invalid EVM wallet address.");

        return new ComplianceProfile
        {
            Id = Guid.NewGuid(),
            InvestorId = investorId,
            WalletAddress = walletAddress
        };
    }

    /// <summary>Stores the issued decentralized identifier.</summary>
    public void SetDid(string did)
    {
        if (string.IsNullOrWhiteSpace(did))
            throw new DomainException("DID cannot be empty.");

        Did = did;
    }

    /// <summary>Stores the serialized attestations payload (JSON).</summary>
    public void SetAttestations(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new DomainException("Attestations payload cannot be empty.");

        AttestationsJson = json;
    }

    /// <summary>Marks the profile as allowlisted on the permissioned token.</summary>
    public void MarkAllowlisted()
    {
        if (IsRevoked)
            throw new DomainException("Cannot allowlist a revoked compliance profile.");

        IsAllowlisted = true;
    }

    /// <summary>Removes the profile from the allowlist (without revoking attestations).</summary>
    public void RemoveFromAllowlist() => IsAllowlisted = false;

    /// <summary>Revokes attestations: clears the allowlist flag and records the reason.</summary>
    public void Revoke(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Revocation reason is required.");

        IsRevoked = true;
        IsAllowlisted = false;
        RevocationReason = reason;
        RaiseEvent(new AttestationsRevokedEvent(InvestorId, reason));
    }
}
