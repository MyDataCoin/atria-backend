using Atria.Domain.Kyc;

namespace Atria.Application.Abstractions;

public sealed record KycSessionRequest(Guid KycProfileId, Guid UserId, string? RedirectUrl);

/// <summary>Hosted-flow session: redirect the user to <see cref="VerificationUrl"/>.</summary>
public sealed record KycSessionResult(string SessionId, string VerificationUrl);

/// <summary>
/// Outcome parsed from a provider callback. <see cref="Pending"/> means a non-terminal or
/// unrelated event (e.g. Didit "In Review" / "In Progress" / a non status.updated event) that
/// is acknowledged but must NOT move the KycProfile state.
/// </summary>
public enum KycDecision { Approved, Declined, Pending }

/// <summary>Parsed result of a provider webhook callback.</summary>
public sealed record KycCallbackResult(
    string ExternalSessionId,
    KycDecision Decision,
    string? Reason,
    string EventId);

/// <summary>
/// Verified identity read back from the provider's decision after approval — the REAL name on
/// the scanned ID document, as opposed to whatever the user self-reported at submit. Any field
/// may be null: some providers expose a split first/last name, some only a full name.
/// </summary>
public sealed record KycVerifiedIdentity(string? FirstName, string? LastName, string? FullName);

/// <summary>
/// The provider's CURRENT verdict on a session, read directly from its API rather than waited for
/// on a webhook.
/// </summary>
/// <remarks>
/// The webhook is the normal path, but it is a delivery — and deliveries are lost. Didit retries a
/// failing endpoint five times and then drops the event for good, which leaves a verification that
/// really did pass sitting in <c>UnderReview</c> with nothing in our data to say why. Being able to
/// ask the provider what it decided is the difference between a stuck profile someone can fix and a
/// stuck profile nobody can.
/// </remarks>
/// <param name="Decision">Terminal outcome, or <see cref="KycDecision.Pending"/> while there is none yet.</param>
/// <param name="RawStatus">The provider's own status string, for logs and for telling an operator what it said.</param>
/// <param name="Reason">Why it was declined, when it was.</param>
/// <param name="Identity">Verified name from the ID document, when the provider exposes one.</param>
public sealed record KycProviderDecision(
    KycDecision Decision, string? RawStatus, string? Reason, KycVerifiedIdentity? Identity);

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

    /// <summary>
    /// Fetches the verified identity (the real name from the ID document) for a decided session,
    /// used to enrich the account after approval. Returns null when the provider exposes no
    /// verified data (e.g. manual review) or it cannot be retrieved — callers must treat this as
    /// best-effort enrichment and never let a null/failure block the approval flow.
    /// </summary>
    Task<KycVerifiedIdentity?> RetrieveVerifiedIdentityAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Asks the provider what it decided about a session. Returns null when the provider has no such
    /// API (e.g. manual review) or it could not be reached — the caller must tell those apart from a
    /// genuine "no decision yet" and never treat an unanswered question as an answer.
    /// </summary>
    Task<KycProviderDecision?> RetrieveDecisionAsync(string sessionId, CancellationToken ct);
}
