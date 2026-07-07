using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Kyc.Dtos;
using Atria.Domain.Consents;
using Atria.Domain.Kyc;

namespace Atria.Application.Kyc.Commands;

/// <summary>Handles <see cref="SubmitKycCommand"/> for the authenticated investor.</summary>
public sealed class SubmitKycCommandHandler
    : IRequestHandler<SubmitKycCommand, Result<KycSubmissionDto>>
{
    private readonly IKycRepository _kyc;
    private readonly IConsentRepository _consents;
    private readonly ICurrentUserService _currentUser;
    private readonly IEnumerable<IKycProviderStrategy> _providers;
    private readonly IUnitOfWork _uow;

    public SubmitKycCommandHandler(
        IKycRepository kyc,
        IConsentRepository consents,
        ICurrentUserService currentUser,
        IEnumerable<IKycProviderStrategy> providers,
        IUnitOfWork uow)
    {
        _kyc = kyc;
        _consents = consents;
        _currentUser = currentUser;
        _providers = providers;
        _uow = uow;
    }

    public async Task<Result<KycSubmissionDto>> Handle(SubmitKycCommand request, CancellationToken ct)
    {
        // Resource owner: the current investor acts on their OWN profile only.
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<KycSubmissionDto>(
                Error.Unauthorized("Kyc.Unauthorized", "Authentication required."));

        // Consent gate: without a recorded acceptance of the CURRENT personal-data notice (ПДН),
        // the KYC profile must not go into processing. Checked before spending a provider session.
        var requiredVersion = ConsentPolicy.CurrentVersion(ConsentType.Pdn);
        var consent = await _consents.GetAsync(userId, ConsentType.Pdn, requiredVersion, ct);
        if (consent is null)
            return Result.Failure<KycSubmissionDto>(Error.Conflict(
                "Kyc.ConsentRequired",
                $"Personal-data consent (ПДН) v{requiredVersion} must be accepted before KYC submission."));

        // Strategy selection by type (never if/else on a string).
        var provider = _providers.FirstOrDefault(p => p.ProviderType == request.Provider);
        if (provider is null)
            return Result.Failure<KycSubmissionDto>(
                Error.Validation("Kyc.UnknownProvider", "The requested KYC provider is not configured."));

        // One profile per user; create on first submit.
        var profile = await _kyc.GetByUserIdAsync(userId, ct);

        // A decided profile cannot be (re)submitted — fail before spending a provider session.
        if (profile is { Status: KycStatus.Approved })
            return Result.Failure<KycSubmissionDto>(
                Error.Conflict("Kyc.AlreadyVerified", "Your identity is already verified."));
        if (profile is { Status: KycStatus.Rejected })
            return Result.Failure<KycSubmissionDto>(
                Error.Conflict("Kyc.Rejected", "Your verification was rejected. Please contact support."));

        var isNew = profile is null;
        profile ??= KycProfile.Create(userId);

        KycSessionResult session;
        try
        {
            session = await provider.CreateSessionAsync(
                new KycSessionRequest(profile.Id, userId, RedirectUrl: null),
                ct);
        }
        catch (KycProviderException ex)
        {
            // The provider (Didit) rejected or could not be reached. Surface a 502 the client can
            // distinguish from a bad request — nothing is persisted, so the user can safely retry.
            return Result.Failure<KycSubmissionDto>(Error.ExternalService(
                "Kyc.ProviderError",
                ex.ProviderStatus is { } status
                    ? $"KYC provider rejected the request (status={status})."
                    : "KYC provider is currently unavailable. Please try again later."));
        }

        if (profile.Status == KycStatus.UnderReview)
            // RESUME/RESTART: the user abandoned an in-progress verification (e.g. closed the
            // hosted flow before finishing). Point the profile at a fresh session; stay UnderReview.
            profile.RefreshSession(request.Provider, session.SessionId, session.VerificationUrl);
        else
            // First submission: domain enforces Pending -> UnderReview (raises KycSubmittedEvent).
            profile.Submit(request.Provider, session.SessionId, session.VerificationUrl,
                request.WalletAddress, request.FullName, request.DocumentNumber, request.Nationality);

        if (isNew)
            await _kyc.AddAsync(profile, ct);
        else
            _kyc.Update(profile);

        await _uow.SaveChangesAsync(ct);

        // Return the hosted verification URL so the client can redirect the user to the provider.
        return new KycSubmissionDto(profile.Id, profile.Status, session.SessionId, session.VerificationUrl);
    }
}
