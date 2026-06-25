namespace Atria.Application.Abstractions;

public sealed record SigningRequest(string OperationType, string UnsignedPayload, string? ChainId);

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
