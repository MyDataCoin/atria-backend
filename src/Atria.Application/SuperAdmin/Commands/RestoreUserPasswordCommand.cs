using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Users;

namespace Atria.Application.SuperAdmin.Commands;

/// <summary>
/// Restores an admin's or realtor's access by clearing the forced-reset flag left by a password
/// reset. Super admin only. A separate, separately-audited action from the reset.
/// </summary>
/// <param name="UserId">The <c>users.id</c> whose access to restore.</param>
public sealed record RestoreUserPasswordCommand(Guid UserId) : IRequest<Result>;

/// <summary>
/// Clears the forced-reset flag on the target admin/realtor account and journals the action. 404
/// when the user does not exist; 409 for an investor (no password) or when no reset is pending.
/// </summary>
public sealed class RestoreUserPasswordCommandHandler
    : IRequestHandler<RestoreUserPasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RestoreUserPasswordCommandHandler(
        IUserRepository users, IAuditWriter audit, IUnitOfWork unitOfWork)
    {
        _users = users;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RestoreUserPasswordCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure(Error.NotFound("user.not_found", "User not found."));

        if (user.Role is not (Role.Admin or Role.Realtor or Role.SuperAdmin))
            return Result.Failure(Error.Conflict(
                "user.no_password", "Only admin and realtor accounts have a password to restore."));

        try
        {
            user.RestorePassword();
        }
        catch (DomainException ex)
        {
            // No reset was pending — nothing to restore.
            return Result.Failure(Error.Conflict("user.no_reset_pending", ex.Message));
        }

        _users.Update(user);

        await _audit.WriteAsync(
            AuditEntities.User, user.Id, AuditEvents.PasswordRestored,
            $"Восстановлен доступ аккаунта ({user.Role})", AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
