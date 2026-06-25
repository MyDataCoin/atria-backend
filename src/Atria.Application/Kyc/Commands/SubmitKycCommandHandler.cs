using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Kyc.Dtos;
using Atria.Domain.Kyc;

namespace Atria.Application.Kyc.Commands;

/// <summary>Handles <see cref="SubmitKycCommand"/> for the authenticated investor.</summary>
public sealed class SubmitKycCommandHandler
    : IRequestHandler<SubmitKycCommand, Result<KycStatusDto>>
{
    private readonly IKycRepository _kyc;
    private readonly ICurrentUserService _currentUser;
    private readonly IEnumerable<IKycProviderStrategy> _providers;
    private readonly IUnitOfWork _uow;

    public SubmitKycCommandHandler(
        IKycRepository kyc,
        ICurrentUserService currentUser,
        IEnumerable<IKycProviderStrategy> providers,
        IUnitOfWork uow)
    {
        _kyc = kyc;
        _currentUser = currentUser;
        _providers = providers;
        _uow = uow;
    }

    public async Task<Result<KycStatusDto>> Handle(SubmitKycCommand request, CancellationToken ct)
    {
        // Resource owner: the current investor acts on their OWN profile only.
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<KycStatusDto>(
                Error.Unauthorized("Kyc.Unauthorized", "Authentication required."));

        // Strategy selection by type (never if/else on a string).
        var provider = _providers.FirstOrDefault(p => p.ProviderType == request.Provider);
        if (provider is null)
            return Result.Failure<KycStatusDto>(
                Error.Validation("Kyc.UnknownProvider", "The requested KYC provider is not configured."));

        // One profile per user; create on first submit.
        var profile = await _kyc.GetByUserIdAsync(userId, ct);
        var isNew = profile is null;
        profile ??= KycProfile.Create(userId);

        var session = await provider.CreateSessionAsync(
            new KycSessionRequest(profile.Id, userId, _currentUser.Email ?? string.Empty, RedirectUrl: null),
            ct);

        // Domain enforces the Pending -> UnderReview transition (raises KycSubmittedEvent).
        profile.Submit(request.Provider, session.SessionId, request.WalletAddress,
            request.FullName, request.DocumentNumber, request.Nationality);

        if (isNew)
            await _kyc.AddAsync(profile, ct);
        else
            _kyc.Update(profile);

        await _uow.SaveChangesAsync(ct);

        return new KycStatusDto(profile.Id, profile.Status, profile.RejectionReason);
    }
}
