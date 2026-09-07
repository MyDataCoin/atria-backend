namespace Atria.Application.Abstractions;

/// <summary>Why an inbound trade event was not accepted as authentic.</summary>
public enum SignatureVerdict
{
    /// <summary>Signature valid and fresh.</summary>
    Valid = 0,

    /// <summary>No shared secret is configured, so nothing can be verified.</summary>
    NotConfigured = 1,

    /// <summary>The request carried no signature header.</summary>
    Missing = 2,

    /// <summary>The signature does not match the body under any accepted key.</summary>
    Invalid = 3,

    /// <summary>
    /// The signature is well-formed but its timestamp is outside the accepted window — a replay of
    /// a request that was authentic once.
    /// </summary>
    Stale = 4
}

/// <summary>
/// Authenticates a trade event delivered by the trading operator.
/// </summary>
/// <remarks>
/// Reporting is built on these events, so an unsigned or unverifiable one is refused rather than
/// recorded with a caveat. Verification is deliberately separate from parsing: the body is
/// authenticated as raw bytes before anything reads a field out of it.
/// </remarks>
public interface IExchangeSignatureVerifier
{
    /// <summary>
    /// Checks a raw body against the signature the operator sent with it.
    /// </summary>
    /// <param name="rawBody">The exact bytes received, before any parsing.</param>
    /// <param name="signature">The signature header value.</param>
    /// <param name="timestamp">The timestamp header value, signed alongside the body.</param>
    SignatureVerdict Verify(string rawBody, string? signature, string? timestamp);
}
