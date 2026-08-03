using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Governance;

namespace Atria.Application.Governance.Commands;

/// <summary>Asks a second person to approve an action that may not be taken alone.</summary>
/// <param name="Kind">What is being asked for.</param>
/// <param name="TargetId">The entity the action applies to.</param>
/// <param name="Reason">Why. Carried into the executed action where it has a meaning (e.g. a ban reason).</param>
public sealed record RequestCriticalActionCommand(CriticalActionKind Kind, Guid TargetId, string? Reason)
    : IRequest<Result<Guid>>;

/// <summary>
/// Opens a dual-approval request. One request per (kind, target) at a time: a second ask for the same
/// thing returns the open one rather than creating a competing request that a colleague might approve
/// twice over.
/// </summary>
public sealed class RequestCriticalActionCommandHandler
    : IRequestHandler<RequestCriticalActionCommand, Result<Guid>>
{
    private readonly ICriticalActionRepository _actions;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public RequestCriticalActionCommandHandler(
        ICriticalActionRepository actions,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _actions = actions;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result<Guid>> Handle(RequestCriticalActionCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<Guid>(
                Error.Unauthorized("criticalAction.unauthorized", "Authentication is required."));

        var now = _clock.UtcNow;

        var pending = await _actions.FindPendingAsync(request.Kind, request.TargetId, ct);
        if (pending is not null && !pending.IsExpiredAt(now))
            return Result.Success(pending.Id);

        if (pending is not null)
        {
            pending.Expire(now);
            _actions.Update(pending);
        }

        var action = CriticalAction.Request(request.Kind, request.TargetId, request.Reason, userId.Value, now);
        await _actions.AddAsync(action, ct);

        await _audit.WriteAsync(
            AuditEntities.CriticalAction, action.Id, AuditEvents.CriticalActionRequested,
            $"Запрошено второе подтверждение: {request.Kind}",
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);

        return Result.Success(action.Id);
    }
}
