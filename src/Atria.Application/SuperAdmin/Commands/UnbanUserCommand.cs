using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;

namespace Atria.Application.SuperAdmin.Commands;

/// <summary>Lifts a ban on a user account. Super admin only.</summary>
/// <param name="UserId">The <c>users.id</c> to unban.</param>
public sealed record UnbanUserCommand(Guid UserId) : IRequest<Result>;

/// <summary>
/// Loads the target user, lifts the ban (idempotent), and journals the action. 404 when the user
/// does not exist.
/// </summary>
public sealed class UnbanUserCommandHandler : IRequestHandler<UnbanUserCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public UnbanUserCommandHandler(IUserRepository users, IAuditWriter audit, IUnitOfWork unitOfWork)
    {
        _users = users;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UnbanUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure(Error.NotFound("user.not_found", "User not found."));

        user.Unban();
        _users.Update(user);

        await _audit.WriteAsync(
            AuditEntities.User, user.Id, AuditEvents.UserUnbanned,
            $"Разблокирован аккаунт ({user.Role})", AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
