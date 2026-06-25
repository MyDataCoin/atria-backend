using Atria.Domain.Kyc;

namespace Atria.Application.Abstractions;

public sealed record KycSessionRequest(Guid KycProfileId, Guid UserId, string Email, string? RedirectUrl);

/// <summary>Hosted-flow session: redirect the user to <see cref="VerificationUrl"/>.</summary>
public sealed record KycSessionResult(string SessionId, string VerificationUrl);

public enum KycDecision { Approved, Declined }

/// <summary>Parsed result of a provider webhook callback.</summary>
public sealed record KycCallbackResult(
    string ExternalSessionId,
    KycDecision Decision,
    string? Reason,
    string EventId);

/// <summary>
/// KYC provider Strategy. Async by design: create a session, the user verifies on
/// the provider side, the provider calls our webhook. Selected by ProviderType
/// through DI (never if/else on a string).
/// </summary>
public interface IKycProviderStrategy
{
    KycProviderType ProviderType { get; }

    Task<KycSessionResult> CreateSessionAsync(KycSessionRequest request, CancellationToken ct);

    /// <summary>Verify the provider signature + freshness of an inbound webhook.</summary>
    bool VerifySignature(WebhookPayload payload);

    /// <summary>Parse a verified webhook into a decision. Never trust an unverified body.</summary>
    KycCallbackResult ParseCallback(WebhookPayload payload);
}
