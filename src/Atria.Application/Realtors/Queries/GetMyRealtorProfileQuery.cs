using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Realtors.Dtos;

namespace Atria.Application.Realtors.Queries;

/// <summary>Returns the current realtor's business profile.</summary>
public sealed record GetMyRealtorProfileQuery : IRequest<Result<RealtorProfileDto>>;

/// <summary>
/// Loads the profile linked to the authenticated realtor. Reports a 404 when no profile row exists
/// yet (profiles are populated out-of-band), so the caller can tell "not set up" from an error.
/// </summary>
public sealed class GetMyRealtorProfileQueryHandler
    : IRequestHandler<GetMyRealtorProfileQuery, Result<RealtorProfileDto>>
{
    private readonly IRealtorProfileRepository _profiles;
    private readonly ICurrentUserService _currentUser;

    public GetMyRealtorProfileQueryHandler(
        IRealtorProfileRepository profiles, ICurrentUserService currentUser)
    {
        _profiles = profiles;
        _currentUser = currentUser;
    }

    public async Task<Result<RealtorProfileDto>> Handle(GetMyRealtorProfileQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<RealtorProfileDto>(
                Error.Unauthorized("realtor.unauthorized", "Authentication required."));

        var profile = await _profiles.GetByUserIdAsync(userId.Value, ct);
        if (profile is null)
            return Result.Failure<RealtorProfileDto>(
                Error.NotFound("realtor.profile_not_found", "Realtor profile not found."));

        return Result.Success(new RealtorProfileDto(
            profile.Id,
            profile.UserId,
            profile.FullName,
            profile.Position,
            profile.WalletAddress,
            profile.CompanyName,
            profile.CompanyRegistrationNumber,
            profile.OfficeAddress));
    }
}
