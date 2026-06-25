namespace Atria.Application.Abstractions;

public sealed record AnchorResult(string TransactionRef, bool Confirmed);

/// <summary>
/// Anchors attestation Merkle roots on chain (Solana for the pilot). A separate
/// EVM anchor is a future <see cref="IChainAnchor"/> implementation and does not
/// block the backend.
/// </summary>
public interface IChainAnchor
{
    Task<AnchorResult> AnchorAsync(string merkleRoot, CancellationToken ct);
}
