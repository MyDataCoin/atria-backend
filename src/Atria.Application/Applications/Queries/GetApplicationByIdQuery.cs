using Atria.Application.Abstractions;
using Atria.Application.Applications.Dtos;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Applications.Queries;

/// <summary>Fetches a single application by id.</summary>
public sealed record GetApplicationByIdQuery(Guid Id) : IRequest<Result<ApplicationDto>>;

/// <summary>
/// Returns the application if the caller owns it OR holds the Compliance/Admin role;
/// otherwise NotFound/Forbidden.
/// </summary>
public sealed class GetApplicationByIdQueryHandler
    : IRequestHandler<GetApplicationByIdQuery, Result<ApplicationDto>>
{
    private readonly IApplicationRepository _applications;
    private readonly ICurrentUserService _currentUser;

    public GetApplicationByIdQueryHandler(
        IApplicationRepository applications,
        ICurrentUserService currentUser)
    {
        _applications = applications;
        _currentUser = currentUser;
    }

    public async Task<Result<ApplicationDto>> Handle(GetApplicationByIdQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<ApplicationDto>(
                Error.Unauthorized("application.unauthorized", "Authentication required."));

        var application = await _applications.GetByIdAsync(request.Id, ct);
        if (application is null)
            return Result.Failure<ApplicationDto>(
                Error.NotFound("application.not_found", "Application not found."));

        // Owner OR Compliance/Admin may read; anyone else is forbidden.
        var isPrivileged = _currentUser.IsInRole(Role.Compliance) || _currentUser.IsInRole(Role.Admin);
        if (application.InvestorId != userId.Value && !isPrivileged)
            return Result.Failure<ApplicationDto>(
                Error.Forbidden("application.forbidden", "You may not view this application."));

        var dto = new ApplicationDto(
            application.Id, application.PropertyId, application.Amount,
            application.Status, application.RejectionReason, application.CreatedAtUtc);

        return Result.Success(dto);
    }
}
