using Atria.Application.Abstractions;
using Atria.Application.Applications.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Applications.Queries;

/// <summary>Lists the current investor's applications.</summary>
public sealed record GetMyApplicationsQuery : IRequest<Result<IReadOnlyList<ApplicationDto>>>;

/// <summary>Returns only the applications owned by the current investor.</summary>
public sealed class GetMyApplicationsQueryHandler
    : IRequestHandler<GetMyApplicationsQuery, Result<IReadOnlyList<ApplicationDto>>>
{
    private readonly IApplicationRepository _applications;
    private readonly ICurrentUserService _currentUser;

    public GetMyApplicationsQueryHandler(
        IApplicationRepository applications,
        ICurrentUserService currentUser)
    {
        _applications = applications;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ApplicationDto>>> Handle(
        GetMyApplicationsQuery request, CancellationToken ct)
    {
        var investorId = _currentUser.UserId;
        if (investorId is null)
            return Result.Failure<IReadOnlyList<ApplicationDto>>(
                Error.Unauthorized("application.unauthorized", "Authentication required."));

        var items = await _applications.GetByInvestorAsync(investorId.Value, ct);

        IReadOnlyList<ApplicationDto> dtos = items
            .Select(a => new ApplicationDto(
                a.Id, a.PropertyId, a.Amount, a.Status, a.RejectionReason, a.CreatedAtUtc))
            .ToList();

        return Result.Success(dtos);
    }
}
