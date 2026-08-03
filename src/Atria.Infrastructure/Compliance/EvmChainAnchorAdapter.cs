using Atria.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Tessera.Core;
using TesseraEvm = Tessera.Chains.Evm;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Anchors attestation roots on an EVM network by driving Tessera's <c>EvmChainAnchor</c>.
///
/// This exists because the two <c>IChainAnchor</c> interfaces in play are not the same interface.
/// ATRIA's speaks in a DID string and a hex root and answers with a transaction reference; Tessera's
/// speaks in <see cref="DidId"/> and raw bytes and answers with a tx id, a slot and a submission
/// time. Substituting one for the other in the container does not compile, let alone work — so the
/// translation lives here, in one place, rather than leaking chain types into the application layer.
/// </summary>
public sealed class EvmChainAnchorAdapter : IChainAnchor
{
    private const int RootLengthBytes = 32;

    private readonly TesseraEvm.EvmChainAnchor _anchor;
    private readonly ILogger<EvmChainAnchorAdapter> _logger;

    public EvmChainAnchorAdapter(TesseraEvm.EvmChainAnchor anchor, ILogger<EvmChainAnchorAdapter> logger)
    {
        _anchor = anchor;
        _logger = logger;
    }

    public async Task<AnchorResult> AnchorAsync(string did, string merkleRoot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(did))
            throw new ArgumentException("DID is required.", nameof(did));
        if (string.IsNullOrWhiteSpace(merkleRoot))
            throw new ArgumentException("Merkle root is required.", nameof(merkleRoot));

        var root = ParseRoot(merkleRoot);

        var result = await _anchor.AnchorRootAsync(new DidId(did), root, ct);

        _logger.LogInformation(
            "Anchored attestation root for {Did} on {ChainId}; tx {TxId}, block {Slot}.",
            did, _anchor.ChainId, result.TxId, result.Slot?.ToString() ?? "<pending>");

        // Confirmed only when the chain gave back a block: the adapter reports what it observed and
        // nothing more. A submitted transaction that has not been mined is not a settled anchor, and
        // callers decide what to do about that rather than being told a comfortable lie.
        return new AnchorResult(result.TxId, Confirmed: result.Slot is not null);
    }

    /// <summary>
    /// Hex root (with or without the 0x prefix) to the exact 32 bytes the registry stores. A root of
    /// any other length is a caller bug, not something to pad or truncate into place: a silently
    /// reshaped root anchors a commitment nobody can reproduce.
    /// </summary>
    private static byte[] ParseRoot(string merkleRoot)
    {
        var hex = merkleRoot.AsSpan();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Merkle root must be hex-encoded.", nameof(merkleRoot), ex);
        }

        if (bytes.Length != RootLengthBytes)
            throw new ArgumentException(
                $"Merkle root must be {RootLengthBytes} bytes; got {bytes.Length}.", nameof(merkleRoot));

        return bytes;
    }
}
