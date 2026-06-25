using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Applications.Commands;

/// <summary>Rejects an application with a reason. Compliance-only operation.</summary>
public sealed record RejectApplicationCommand(Guid Id, string Reason) : IRequest<Result>;

/// <summary>Compliance-only: transitions the application via <c>Reject(reason)</c> (State pattern).</summary>
public sealed class RejectApplicationCommandHandler
    : IRequestHandler<RejectApplicationCommand, Result>
{
    private readonly IApplicationRepository _applications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public RejectApplicationCommandHandler(
        IApplicationRepository applications,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _applications = applications;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(RejectApplicationCommand request, CancellationToken ct)
    {
        // Only Compliance may reject applications.
        if (!_currentUser.IsInRole(Role.Compliance))
            return Result.Failure(Error.Forbidden("application.forbidden", "Compliance role required."));

        var application = await _applications.GetByIdAsync(request.Id, ct);
        if (application is null)
            return Result.Failure(Error.NotFound("application.not_found", "Application not found."));

        application.Reject(request.Reason);
        _applications.Update(application);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
