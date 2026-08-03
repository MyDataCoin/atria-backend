using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Settings for anchoring attestation roots on an EVM network through Tessera's adapter.
/// </summary>
/// <remarks>
/// <para>
/// This section holds a private key, which is a deliberate exception to the rule that no key lives
/// in this process. Tessera's adapter signs its own transactions and has no external-signer seam, so
/// the choice was between rewriting someone else's adapter and giving it a key that cannot do
/// damage. The second is what the deployment set up: this account is an agent of the identity
/// registry and the allowlist and <b>nothing else</b> — it holds no role on the token contract, so
/// it cannot mint, burn, freeze, pause or move anyone's shares. Losing it means an attacker can
/// allowlist addresses and write anchor roots; it does not mean they can touch the issue.
/// </para>
/// <para>
/// It must never be the custody key, the minter key or the admin multisig. Supply it through the
/// same guarded-secret path as the other secrets, never in a committed file.
/// </para>
/// </remarks>
public sealed class EvmAnchorOptions
{
    public const string SectionName = "Blockchain:Anchor";

    /// <summary>JSON-RPC endpoint of the network the issue lives on.</summary>
    [Required]
    [Url]
    public string RpcUrl { get; init; } = null!;

    /// <summary>EIP-155 chain id (97 = BNB testnet, 56 = BNB mainnet).</summary>
    [Range(1, long.MaxValue)]
    public long ChainId { get; init; }

    /// <summary>Deployed <c>IdentityRegistry</c> address.</summary>
    [Required]
    public string IdentityRegistryAddress { get; init; } = null!;

    /// <summary>
    /// Key of the registry/allowlist agent that signs anchor writes. Scoped as described above.
    /// </summary>
    [Required]
    public string AgentPrivateKey { get; init; } = null!;

    /// <summary>
    /// BNB Chain rejects EIP-1559 transactions with a zero priority fee, which is what the automatic
    /// fee path produces there, so legacy pricing is the default rather than an exotic switch.
    /// </summary>
    public bool UseLegacyGasPricing { get; init; } = true;
}
