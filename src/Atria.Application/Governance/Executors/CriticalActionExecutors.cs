using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Governance;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Governance.Executors;

/// <summary>
/// Blocks an investor once a second person has approved it. Cutting an investor off from their
/// holdings is not reversible in any meaningful sense — the shares stay where they are and the person
/// loses access to the platform — so it never happens on one signature.
/// </summary>
public sealed class BlockInvestorExecutor : ICriticalActionExecutor
{
    private readonly IUserRepository _users;
    private readonly IAuditWriter _audit;

    public BlockInvestorExecutor(IUserRepository users, IAuditWriter audit)
    {
        _users = users;
        _audit = audit;
    }

    public CriticalActionKind Kind => CriticalActionKind.InvestorBlock;

    public async Task<Result> ExecuteAsync(CriticalAction action, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(action.TargetId, ct);
        if (user is null)
            return Result.Failure(Error.NotFound("user.not_found", "User not found."));

        user.Ban(action.Reason);
        _users.Update(user);

        var summary = string.IsNullOrWhiteSpace(user.BanReason)
            ? $"Заблокирован аккаунт ({user.Role}) по двум подтверждениям"
            : $"Заблокирован аккаунт ({user.Role}) по двум подтверждениям: {user.BanReason}";

        await _audit.WriteAsync(AuditEntities.User, user.Id, AuditEvents.UserBanned, summary, AuditSeverity.Alert, ct);

        return Result.Success();
    }
}
