namespace Atria.Application.Abstractions;

/// <summary>Outcome of an anchoring write.</summary>
/// <param name="TransactionRef">Chain-native transaction reference.</param>
/// <param name="Confirmed">
/// Whether finality was observed. False means submitted and not yet verified on chain — never
/// treat it as settled.
/// </param>
public sealed record AnchorResult(string TransactionRef, bool Confirmed);

/// <summary>
/// Anchors an investor's attestation Merkle root on chain. Only roots are anchored — never identity
/// data.
/// </summary>
/// <remarks>
/// The DID is part of the call because the on-chain registry keys anchors by DID: a root with no
/// subject cannot be written to it, and the account that registers a DID owns it for every later
/// update. This is why the interface is not just "anchor this string".
/// </remarks>
public interface IChainAnchor
{
    /// <param name="did">The DID the root belongs to.</param>
    /// <param name="merkleRoot">Hex-encoded 32-byte attestation root.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AnchorResult> AnchorAsync(string did, string merkleRoot, CancellationToken ct);
}
