using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// LOCAL in-house implementation of <see cref="ITesseraComplianceService"/>. The
/// Tessera.* SDK packages are not publicly resolvable, so this follows the Tessera
/// principles directly: identity data stays OFF chain (persisted on
/// <see cref="ComplianceProfile"/>, PII handled by the existing encryption), only
/// attestation Merkle roots are anchored (via <see cref="IChainAnchor"/>), and the
/// permissioned BEP-20 allowlist is updated via <see cref="IBlockchainOperationQueue"/>.
/// </summary>
public sealed class TesseraComplianceService : ITesseraComplianceService
{
    private readonly IComplianceRepository _profiles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChainAnchor _chainAnchor;
    private readonly IBlockchainOperationQueue _operationQueue;
    private readonly TesseraOptions _options;
    private readonly ILogger<TesseraComplianceService> _logger;

    public TesseraComplianceService(
        IComplianceRepository profiles,
        IUnitOfWork unitOfWork,
        IChainAnchor chainAnchor,
        IBlockchainOperationQueue operationQueue,
        IOptions<TesseraOptions> options,
        ILogger<TesseraComplianceService> logger)
    {
        _profiles = profiles;
        _unitOfWork = unitOfWork;
        _chainAnchor = chainAnchor;
        _operationQueue = operationQueue;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AttestationIssueResult> IssueDidAndAttestationsAsync(
        Guid investorId,
        string walletAddress,
        IReadOnlyCollection<AttestationType> attestations,
        CancellationToken ct)
    {
        var profile = await _profiles.GetByInvestorAsync(investorId, ct)
                      ?? throw new InvalidOperationException(
                          $"No compliance profile for investor {investorId}.");

        // Deterministic DID derived from the issuer DID + investor id (off-chain).
        // NOTE: a real Tessera Issuer would mint the DID + signed verifiable
        // credentials via the SDK; here we derive a stable did:atria identifier.
        var did = DeriveDid(investorId);
        profile.SetDid(did);

        // Record attestation TYPES (the identity values stay encrypted off chain).
        var wireTypes = attestations.Select(a => a.ToWire()).ToArray();
        var attestationsJson = JsonSerializer.Serialize(new
        {
            did,
            issuer = _options.IssuerDid,
            types = wireTypes,
            issuedAtUtc = DateTime.UtcNow
        });
        profile.SetAttestations(attestationsJson);

        _profiles.Update(profile);
        await _unitOfWork.SaveChangesAsync(ct);

        // Compute a Merkle-root-ish hash over the attestation set and anchor it.
        var merkleRoot = ComputeAttestationRoot(did, wireTypes);
        var anchorResult = await _chainAnchor.AnchorAsync(merkleRoot, ct);

        _logger.LogInformation(
            "Issued DID and {AttestationCount} attestation(s) for investor {InvestorId}; anchored root {TransactionRef}.",
            wireTypes.Length, investorId, anchorResult.TransactionRef);

        return new AttestationIssueResult(did, wireTypes, anchorResult.TransactionRef);
    }

    public async Task<bool> VerifyPresentationAsync(Guid investorId, string policyId, CancellationToken ct)
    {
        var profile = await _profiles.GetByInvestorAsync(investorId, ct);
        if (profile is null)
            return false;

        // NOTE: a real Tessera Verifier would validate the holder's verifiable
        // presentation (signatures, schema, freshness) against the policy. Here we
        // check the locally-held profile state against the configured policy.
        var policyMatches = string.Equals(policyId, _options.PolicyId, StringComparison.Ordinal);

        var verified = policyMatches
                       && !profile.IsRevoked
                       && !string.IsNullOrWhiteSpace(profile.Did)
                       && !string.IsNullOrWhiteSpace(profile.AttestationsJson);

        _logger.LogInformation(
            "Presentation verification for investor {InvestorId} against policy {PolicyId}: {Verified}.",
            investorId, policyId, verified);

        return verified;
    }

    public Task AddToAllowlistAsync(string walletAddress, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { walletAddress });
        // Idempotency key ties the queued op to the wallet + operation kind.
        var key = $"allowlist-add:{walletAddress}";
        return _operationQueue.EnqueueAsync(BlockchainOperationType.AllowlistAdd, payload, key, ct);
    }

    public Task RemoveFromAllowlistAsync(string walletAddress, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { walletAddress });
        var key = $"allowlist-remove:{walletAddress}";
        return _operationQueue.EnqueueAsync(BlockchainOperationType.AllowlistRemove, payload, key, ct);
    }

    public async Task RevokeAttestationsAsync(Guid investorId, string reason, CancellationToken ct)
    {
        var profile = await _profiles.GetByInvestorAsync(investorId, ct);
        if (profile is null)
            return;

        // NOTE: a real Tessera Issuer would bump the on-chain/off-chain revocation
        // registry for the issued credentials; the domain Revoke() records the bump
        // (sets IsRevoked, clears allowlist flag, raises AttestationsRevokedEvent).
        profile.Revoke(reason);
        _profiles.Update(profile);
        await _unitOfWork.SaveChangesAsync(ct);

        // Drop the wallet from the permissioned allowlist on chain.
        if (!string.IsNullOrWhiteSpace(profile.WalletAddress))
            await RemoveFromAllowlistAsync(profile.WalletAddress, ct);

        _logger.LogInformation(
            "Revoked attestations for investor {InvestorId} (reason: {Reason}).",
            investorId, reason);
    }

    public async Task<string> AnchorMerkleRootAsync(string merkleRoot, CancellationToken ct)
    {
        var result = await _chainAnchor.AnchorAsync(merkleRoot, ct);
        return result.TransactionRef;
    }

    /// <summary>Derives a stable <c>did:atria:&lt;hash&gt;</c> identifier from issuer + investor id.</summary>
    private string DeriveDid(Guid investorId)
    {
        var seed = $"{_options.IssuerDid}:{investorId:N}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        return $"did:atria:{hash}";
    }

    /// <summary>
    /// Computes a Merkle-root-ish hash over the (sorted) attestation leaves. Identity
    /// values are never included — only the DID and attestation type strings.
    /// </summary>
    private static string ComputeAttestationRoot(string did, IReadOnlyCollection<string> wireTypes)
    {
        // Leaf = H(did || type); ordered for a deterministic root.
        var leaves = wireTypes
            .OrderBy(t => t, StringComparer.Ordinal)
            .Select(t => SHA256.HashData(Encoding.UTF8.GetBytes($"{did}|{t}")))
            .ToList();

        if (leaves.Count == 0)
            leaves.Add(SHA256.HashData(Encoding.UTF8.GetBytes(did)));

        // Pairwise fold until a single root remains.
        while (leaves.Count > 1)
        {
            var next = new List<byte[]>((leaves.Count + 1) / 2);
            for (var i = 0; i < leaves.Count; i += 2)
            {
                var left = leaves[i];
                var right = i + 1 < leaves.Count ? leaves[i + 1] : leaves[i];
                var combined = new byte[left.Length + right.Length];
                Buffer.BlockCopy(left, 0, combined, 0, left.Length);
                Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);
                next.Add(SHA256.HashData(combined));
            }

            leaves = next;
        }

        return Convert.ToHexString(leaves[0]).ToLowerInvariant();
    }
}
