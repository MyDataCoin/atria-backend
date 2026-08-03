namespace Atria.Application.Abstractions;

/// <summary>What the external signer is asked to sign and submit.</summary>
/// <param name="OperationType">The operation being performed, by name.</param>
/// <param name="UnsignedPayload">Operation payload, as JSON.</param>
/// <param name="ChainId">Chain the operation targets. Resolved from the issue, never from a global setting.</param>
/// <param name="TokenContractAddress">
/// The issue's own token contract. Null for operations that touch shared contracts (the allowlist,
/// the identity registry) rather than a single issue's token.
/// </param>
public sealed record SigningRequest(
    string OperationType, string UnsignedPayload, string? ChainId, string? TokenContractAddress = null);

public sealed record SigningResult(string SignedPayload, string? SubmissionReference);

/// <summary>
/// External signer boundary (KMS / HSM / custody). The backend builds and submits
/// a signing request; the private key NEVER lives in this process or in config.
/// Critical operations are designed for multisig on the signer side.
/// </summary>
public interface IBlockchainSigner
{
    Task<SigningResult> SignAndSubmitAsync(SigningRequest request, CancellationToken ct);
}
