namespace Atria.Application.Abstractions;

/// <summary>
/// Raw inbound webhook as received by the API. Strategies verify the signature
/// and parse it; the body is NEVER trusted as a command — it only moves State.
/// </summary>
public sealed record WebhookPayload(
    string RawBody,
    IReadOnlyDictionary<string, string> Headers,
    string? Signature,
    DateTimeOffset? Timestamp,
    string? SourceIp);
