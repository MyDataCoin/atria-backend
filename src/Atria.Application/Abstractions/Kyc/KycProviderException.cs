namespace Atria.Application.Abstractions;

/// <summary>
/// Raised when a KYC provider cannot create or verify a session: either the transport failed
/// (unreachable / timeout) or the provider replied non-2xx (e.g. Didit 403 = missing/invalid/
/// revoked x-api-key, 400 = missing/invalid workflow_id or body). Carries the provider HTTP
/// status (when known) so the caller can surface a precise 502 instead of a generic 500.
/// The message is safe to log but must never leak provider internals to the client.
/// </summary>
public sealed class KycProviderException : Exception
{
    /// <summary>The provider's HTTP status code, or <c>null</c> for a transport/config failure.</summary>
    public int? ProviderStatus { get; }

    public KycProviderException(int? providerStatus, string message) : base(message)
        => ProviderStatus = providerStatus;

    public KycProviderException(int? providerStatus, string message, Exception innerException)
        : base(message, innerException)
        => ProviderStatus = providerStatus;
}
