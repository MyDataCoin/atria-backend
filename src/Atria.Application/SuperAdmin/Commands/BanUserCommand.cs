using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;

namespace Atria.Application.SuperAdmin.Commands;

/// <summary>Bans a user account so it can no longer authenticate. Super admin only.</summary>
/// <param name="UserId">The <c>users.id</c> to ban (investor or realtor).</param>
/// <param name="Reason">Optional reason shown to the user on the blocked screen and journalled.</param>
public sealed record BanUserCommand(Guid UserId, string? Reason = null) : IRequest<Result>;

/// <summary>
/// Loads the target user, bans it (idempotent), and journals the action. 404 when the user does not
/// exist. The ban is enforced at login: a banned account is refused a token.
/// </summary>
public sealed class BanUserCommandHandler : IRequestHandler<BanUserCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public BanUserCommandHandler(IUserRepository users, IAuditWriter audit, IUnitOfWork unitOfWork)
    {
        _users = users;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(BanUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure(Error.NotFound("user.not_found", "User not found."));

        user.Ban(request.Reason);
        _users.Update(user);

        // Include the reason in the audit summary when one was given.
        var summary = string.IsNullOrWhiteSpace(user.BanReason)
            ? $"Заблокирован аккаунт ({user.Role})"
            : $"Заблокирован аккаунт ({user.Role}): {user.BanReason}";

        await _audit.WriteAsync(
            AuditEntities.User, user.Id, AuditEvents.UserBanned,
            summary, AuditSeverity.Alert, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
