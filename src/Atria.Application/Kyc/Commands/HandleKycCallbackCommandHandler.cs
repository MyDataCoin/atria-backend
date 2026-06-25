using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Kyc;

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

    public HandleKycCallbackCommandHandler(
        IEnumerable<IKycProviderStrategy> providers,
        IKycRepository kyc,
        IProcessedEventStore processed,
        IUnitOfWork uow)
    {
        _providers = providers;
        _kyc = kyc;
        _processed = processed;
        _uow = uow;
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

        var profile = await _kyc.GetBySessionIdAsync(callback.ExternalSessionId, ct);
        if (profile is null)
            return Result.Failure(
                Error.NotFound("Kyc.NotFound", "No KYC profile matches the callback session."));

        // Parsed decision only moves State; the State pattern enforces validity.
        switch (callback.Decision)
        {
            case KycDecision.Approved:
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
}
