using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Kyc;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Kyc.Commands;

/// <summary>
/// Handles <see cref="HandleKycCallbackCommand"/>. Verifies the signature, parses
/// the decision, and applies it exactly-once (idempotent on the parsed EventId).
/// </summary>
public sealed class HandleKycCallbackCommandHandler : IRequestHandler<HandleKycCallbackCommand, Result>
{
    private readonly IEnumerable<IKycProviderStrategy> _providers;
    private readonly IKycRepository _kyc;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<HandleKycCallbackCommandHandler> _logger;

    public HandleKycCallbackCommandHandler(
        IEnumerable<IKycProviderStrategy> providers,
        IKycRepository kyc,
        IProcessedEventStore processed,
        IUnitOfWork uow,
        ILogger<HandleKycCallbackCommandHandler> logger)
    {
        _providers = providers;
        _kyc = kyc;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result> Handle(HandleKycCallbackCommand request, CancellationToken ct)
    {
        // Strategy selection by provider name -> ProviderType (never if/else on a string).
        if (!Enum.TryParse<KycProviderType>(request.Provider, ignoreCase: true, out var providerType))
            return Result.Failure(
                Error.Validation("Kyc.UnknownProvider", "The webhook provider is not recognized."));

        var provider = _providers.FirstOrDefault(p => p.ProviderType == providerType);
        if (provider is null)
            return Result.Failure(
                Error.Validation("Kyc.UnknownProvider", "The webhook provider is not configured."));

        // The body is untrusted until the signature is verified.
        if (!provider.VerifySignature(request.Payload))
            return Result.Failure(
                Error.Unauthorized("Kyc.InvalidSignature", "Webhook signature verification failed."));

        var callback = provider.ParseCallback(request.Payload);

        // Exactly-once: at-least-once delivery means a retry must be a no-op.
        var key = IdempotencyKey.For(this, callback.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return Result.Success();

        // Non-terminal / unrelated events (e.g. "In Review", "In Progress", or a non
        // status.updated event family) are acknowledged with 2xx but do not move state.
        if (callback.Decision == KycDecision.Pending)
            return Result.Success();

        var profile = await _kyc.GetBySessionIdAsync(callback.ExternalSessionId, ct);
        if (profile is null)
            return Result.Failure(
                Error.NotFound("Kyc.NotFound", "No KYC profile matches the callback session."));

        // Parsed decision only moves State; the State pattern enforces validity.
        switch (callback.Decision)
        {
            case KycDecision.Approved:
                // Pull the VERIFIED name from the provider's decision (the real name on the ID
                // document) into the profile before approving, replacing the self-reported one.
                await ApplyVerifiedNameAsync(provider, profile, callback.ExternalSessionId, ct);
                profile.Approve();
                break;
            case KycDecision.Declined:
                profile.Reject(callback.Reason ?? "Declined by provider.");
                break;
        }

        _kyc.Update(profile);
        await _processed.MarkProcessedAsync(key, ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <summary>
    /// Best-effort enrichment: overwrites the profile's <c>FullName</c> with the verified name from
    /// the provider's decision. A missing/failed retrieval keeps the self-reported name and must
    /// never block approval — verification already succeeded on the provider side.
    /// </summary>
    private async Task ApplyVerifiedNameAsync(
        IKycProviderStrategy provider, KycProfile profile, string sessionId, CancellationToken ct)
    {
        KycVerifiedIdentity? identity;
        try
        {
            identity = await provider.RetrieveVerifiedIdentityAsync(sessionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to retrieve verified identity for KYC profile {KycProfileId}; keeping self-reported name.",
                profile.Id);
            return;
        }

        var fullName = identity is null ? null : ComposeFullName(identity);
        if (!string.IsNullOrWhiteSpace(fullName))
            profile.SetVerifiedFullName(fullName);
    }

    /// <summary>Prefers a single verified full name; otherwise joins the split first/last parts.</summary>
    private static string? ComposeFullName(KycVerifiedIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.FullName))
            return identity.FullName.Trim();

        var joined = string.Join(' ', new[] { identity.FirstName, identity.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
