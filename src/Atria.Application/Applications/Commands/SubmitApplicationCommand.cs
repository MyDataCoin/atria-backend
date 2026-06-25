using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Applications.Commands;

/// <summary>Submits the investor's own draft application for review.</summary>
public sealed record SubmitApplicationCommand(Guid Id) : IRequest<Result>;

/// <summary>Owner-only: transitions the application via <c>Submit()</c> (State pattern).</summary>
public sealed class SubmitApplicationCommandHandler
    : IRequestHandler<SubmitApplicationCommand, Result>
{
    private readonly IApplicationRepository _applications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public SubmitApplicationCommandHandler(
        IApplicationRepository applications,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _applications = applications;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(SubmitApplicationCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure(Error.Unauthorized("application.unauthorized", "Authentication required."));

        var application = await _applications.GetByIdAsync(request.Id, ct);
        if (application is null)
            return Result.Failure(Error.NotFound("application.not_found", "Application not found."));

        // Resource ownership: an investor may only submit their OWN application.
        if (application.InvestorId != userId.Value)
            return Result.Failure(Error.Forbidden("application.forbidden", "You do not own this application."));

        application.Submit();
        _applications.Update(application);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
