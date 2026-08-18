using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Kyc.Dtos;
using Atria.Domain.Kyc;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Kyc.Commands;

/// <summary>Asks the KYC provider what it decided about a profile's session, and applies it. Operator only.</summary>
/// <param name="ProfileId">The KYC profile to reconcile with its provider.</param>
public sealed record SyncKycFromProviderCommand(Guid ProfileId) : IRequest<Result<KycStatusDto>>;

/// <summary>
/// Pulls a verification's verdict straight from the provider instead of waiting for a webhook that
/// may never arrive.
/// </summary>
/// <remarks>
/// <para>
/// The webhook is the normal path and stays the normal path. But it is a delivery: Didit retries a
/// failing endpoint five times and then drops the event permanently. A verification the user really
/// did pass then sits in <c>UnderReview</c> forever, the investor cannot buy, and nothing in our data
/// says why — the platform simply never heard. This is the way out of that, and it is deliberately a
/// pull: the provider is the authority on its own decision, so we ask it rather than guess.
/// </para>
/// <para>
/// Idempotent by nature. A profile already in a terminal state is returned as-is, and re-running
/// against an undecided session changes nothing.
/// </para>
/// </remarks>
public sealed class SyncKycFromProviderCommandHandler
    : IRequestHandler<SyncKycFromProviderCommand, Result<KycStatusDto>>
{
    private readonly IKycRepository _kyc;
    private readonly IEnumerable<IKycProviderStrategy> _providers;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SyncKycFromProviderCommandHandler> _logger;

    public SyncKycFromProviderCommandHandler(
        IKycRepository kyc,
        IEnumerable<IKycProviderStrategy> providers,
        IUnitOfWork uow,
        ILogger<SyncKycFromProviderCommandHandler> logger)
    {
        _kyc = kyc;
        _providers = providers;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<KycStatusDto>> Handle(SyncKycFromProviderCommand request, CancellationToken ct)
    {
        var profile = await _kyc.GetByIdAsync(request.ProfileId, ct);
        if (profile is null)
            return Result.Failure<KycStatusDto>(Error.NotFound("Kyc.NotFound", "KYC profile not found."));

        // Already decided: nothing to reconcile, and re-deciding a settled profile is exactly what
        // this must not do.
        if (profile.Status is KycStatus.Approved or KycStatus.Rejected)
            return Result.Success(ToDto(profile));

        if (string.IsNullOrWhiteSpace(profile.ProviderSessionId))
            return Result.Failure<KycStatusDto>(Error.Conflict(
                "Kyc.NoSession", "This profile has no provider session to reconcile against."));

        var provider = _providers.FirstOrDefault(p => p.ProviderType == profile.Provider);
        if (provider is null)
            return Result.Failure<KycStatusDto>(Error.Validation(
                "Kyc.UnknownProvider", "The profile's KYC provider is not configured."));

        var decision = await provider.RetrieveDecisionAsync(profile.ProviderSessionId, ct);
        if (decision is null)
            // The provider could not be asked. Reporting "still under review" here would be a
            // fabricated answer to a question nobody managed to put.
            return Result.Failure<KycStatusDto>(Error.ExternalService(
                "Kyc.ProviderUnreachable",
                "Could not read the decision from the provider. Check the provider credentials and try again."));

        switch (decision.Decision)
        {
            case KycDecision.Approved:
                ApplyVerifiedName(profile, decision.Identity);
                profile.Approve();
                _logger.LogInformation(
                    "KYC profile {KycProfileId} approved from a provider pull (status={Status}).",
                    profile.Id, decision.RawStatus);
                break;

            case KycDecision.Declined:
                profile.Reject(decision.Reason ?? "Declined by provider.");
                _logger.LogInformation(
                    "KYC profile {KycProfileId} rejected from a provider pull (status={Status}).",
                    profile.Id, decision.RawStatus);
                break;

            default:
                // Genuinely undecided on the provider side. Say so plainly rather than move state.
                _logger.LogInformation(
                    "KYC profile {KycProfileId} still undecided at the provider (status={Status}).",
                    profile.Id, decision.RawStatus);
                return Result.Success(ToDto(profile));
        }

        _kyc.Update(profile);
        await _uow.SaveChangesAsync(ct);

        return Result.Success(ToDto(profile));
    }

    // Operator-facing read: unlike the investor's own status, it always carries the session so a
    // stuck profile can be chased up with the provider by id.
    private static KycStatusDto ToDto(KycProfile profile)
        => new(profile.Id, profile.Status, profile.RejectionReason,
            profile.ProviderSessionId, profile.VerificationUrl, profile.FullName, profile.WalletAddress);

    /// <summary>
    /// Writes the verified name from the ID document. Best-effort: a provider that returned a
    /// decision but no name must not block applying that decision.
    /// </summary>
    private static void ApplyVerifiedName(KycProfile profile, KycVerifiedIdentity? identity)
    {
        var fullName = KycVerifiedName.Compose(identity);
        if (!string.IsNullOrWhiteSpace(fullName))
            profile.SetVerifiedFullName(fullName);
    }
}
