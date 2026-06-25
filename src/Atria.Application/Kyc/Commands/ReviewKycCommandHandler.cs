using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Kyc.Commands;

/// <summary>Handles <see cref="ReviewKycCommand"/> — Compliance-only decision.</summary>
public sealed class ReviewKycCommandHandler : IRequestHandler<ReviewKycCommand, Result>
{
    private readonly IKycRepository _kyc;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _uow;

    public ReviewKycCommandHandler(
        IKycRepository kyc,
        ICurrentUserService currentUser,
        IUnitOfWork uow)
    {
        _kyc = kyc;
        _currentUser = currentUser;
        _uow = uow;
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
}
