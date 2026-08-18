using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Kyc.Commands;

/// <summary>Handles <see cref="ReviewKycCommand"/> — Compliance-only decision.</summary>
public sealed class ReviewKycCommandHandler : IRequestHandler<ReviewKycCommand, Result>
{
    private readonly IKycRepository _kyc;
    private readonly ICurrentUserService _currentUser;
    private readonly IEnumerable<IKycProviderStrategy> _providers;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ReviewKycCommandHandler> _logger;

    public ReviewKycCommandHandler(
        IKycRepository kyc,
        ICurrentUserService currentUser,
        IEnumerable<IKycProviderStrategy> providers,
        IUnitOfWork uow,
        ILogger<ReviewKycCommandHandler> logger)
    {
        _kyc = kyc;
        _currentUser = currentUser;
        _providers = providers;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result> Handle(ReviewKycCommand request, CancellationToken ct)
    {
        // Authorization: reviewing KYC is reserved for Compliance officers.
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(Error.Unauthorized("Kyc.Unauthorized", "Authentication required."));
        if (!_currentUser.IsInRole(Role.Compliance))
            return Result.Failure(Error.Forbidden("Kyc.Forbidden", "Only Compliance may review KYC."));

        var profile = await _kyc.GetByIdAsync(request.KycId, ct);
        if (profile is null)
            return Result.Failure(Error.NotFound("Kyc.NotFound", "KYC profile not found."));

        // State pattern enforces valid transitions (only UnderReview is decidable).
        if (request.Approve)
        {
            // Same enrichment as the webhook path. Without it a manually approved profile kept an
            // empty name forever: the verified name only ever arrived on the webhook, so approving
            // by hand — which is exactly what an operator does when the webhook did NOT arrive —
            // produced a verified investor nobody could name.
            await ApplyVerifiedNameAsync(profile, ct);
            profile.Approve();
        }
        else
        {
            var reason = request.Reason;
            if (string.IsNullOrWhiteSpace(reason))
                return Result.Failure(
                    Error.Validation("Kyc.ReasonRequired", "A rejection reason is required."));
            profile.Reject(reason);
        }

        _kyc.Update(profile);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <summary>
    /// Best-effort: writes the verified name from the provider's decision. A provider that cannot be
    /// reached must never block a decision an operator has already made.
    /// </summary>
    private async Task ApplyVerifiedNameAsync(Domain.Kyc.KycProfile profile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.ProviderSessionId))
            return;

        var provider = _providers.FirstOrDefault(p => p.ProviderType == profile.Provider);
        if (provider is null)
            return;

        KycVerifiedIdentity? identity;
        try
        {
            identity = await provider.RetrieveVerifiedIdentityAsync(profile.ProviderSessionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to retrieve verified identity for KYC profile {KycProfileId} on manual approval.",
                profile.Id);
            return;
        }

        var fullName = KycVerifiedName.Compose(identity);
        if (!string.IsNullOrWhiteSpace(fullName))
            profile.SetVerifiedFullName(fullName);
    }
}
