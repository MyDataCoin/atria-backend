using Atria.Domain.Compliance;

namespace Atria.Application.Abstractions;

public sealed record AttestationIssueResult(
    string Did,
    IReadOnlyList<string> IssuedAttestationTypes,
    string? AnchoredRoot);

/// <summary>
/// Compliance/Web3 facade over the Tessera Holder/Issuer/Verifier capabilities.
/// Identity data is NEVER written on chain — only attestation Merkle roots are
/// anchored. The wallet allowlist is a permissioned BEP-20 control.
/// </summary>
public interface ITesseraComplianceService
{
    /// <summary>Issue a DID + attestations (kyc_verified, resident, phone_verified) to the investor.</summary>
    Task<AttestationIssueResult> IssueDidAndAttestationsAsync(
        Guid investorId,
        string walletAddress,
        IReadOnlyCollection<AttestationType> attestations,
        CancellationToken ct);

    /// <summary>Verify the investor's presentation against the project policy.</summary>
    Task<bool> VerifyPresentationAsync(Guid investorId, string policyId, CancellationToken ct);

    Task AddToAllowlistAsync(string walletAddress, CancellationToken ct);
    Task RemoveFromAllowlistAsync(string walletAddress, CancellationToken ct);

    /// <summary>Bump the revocation registry and drop the investor's attestations.</summary>
    Task RevokeAttestationsAsync(Guid investorId, string reason, CancellationToken ct);

    /// <summary>Anchor an attestation Merkle root; returns the anchor reference.</summary>
    Task<string> AnchorMerkleRootAsync(string merkleRoot, CancellationToken ct);
}
