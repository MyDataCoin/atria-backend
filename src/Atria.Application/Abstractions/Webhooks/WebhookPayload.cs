namespace Atria.Application.Abstractions;

/// <summary>
/// Raw inbound webhook as received by the API. Strategies verify the signature
/// and parse it; the body is NEVER trusted as a command — it only moves State.
/// </summary>
/// <param name="RawBody">The exact, unparsed request body (needed for byte-accurate signature verification).</param>
/// <param name="Headers">All request headers, keyed case-insensitively.</param>
/// <param name="Signature">The provider's signature header value, if present (used to authenticate the body).</param>
/// <param name="Timestamp">The event timestamp from the provider, if present (used for replay protection).</param>
/// <param name="SourceIp">Remote IP address the request arrived from, if known.</param>
public sealed record WebhookPayload(
    string RawBody,
    IReadOnlyDictionary<string, string> Headers,
    string? Signature,
    DateTimeOffset? Timestamp,
    string? SourceIp);
