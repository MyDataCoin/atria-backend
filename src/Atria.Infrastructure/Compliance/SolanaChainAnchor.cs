using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Pilot <see cref="IChainAnchor"/>: anchors attestation Merkle roots on Solana.
/// This pilot stub returns a deterministic transaction reference derived from the
/// root + configured network so behaviour is reproducible in tests/dev. Only roots
/// are anchored — never identity data.
/// </summary>
public sealed class SolanaChainAnchor : IChainAnchor
{
    private readonly BlockchainOptions _options;
    private readonly ILogger<SolanaChainAnchor> _logger;

    public SolanaChainAnchor(IOptions<BlockchainOptions> options, ILogger<SolanaChainAnchor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<AnchorResult> AnchorAsync(string merkleRoot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(merkleRoot))
            throw new ArgumentException("Merkle root is required.", nameof(merkleRoot));

        // NOTE: a real implementation would submit a memo/anchor instruction to the
        // Solana cluster (BlockchainOptions.AnchorNetwork) via the external signer and
        // return the actual transaction signature. Here we derive a deterministic ref.
        var seed = $"{_options.AnchorNetwork}:{merkleRoot}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        var txRef = $"sol_{_options.AnchorNetwork}_{hash}";

        _logger.LogInformation(
            "Anchored Merkle root on {AnchorNetwork} with reference {TransactionRef}.",
            _options.AnchorNetwork, txRef);

        // Confirmed=false: the pilot stub does not wait for finality.
        return Task.FromResult(new AnchorResult(txRef, Confirmed: false));
    }
}
