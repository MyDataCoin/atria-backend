using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Governance;

namespace Atria.Application.Governance.Commands;

/// <summary>Approves someone else's request and carries the action out.</summary>
/// <param name="Id">The pending request.</param>
public sealed record ApproveCriticalActionCommand(Guid Id) : IRequest<Result>;

/// <summary>Declines someone else's request, with a reason.</summary>
/// <param name="Id">The pending request.</param>
/// <param name="Note">Why it is being declined. Required.</param>
public sealed record RejectCriticalActionCommand(Guid Id, string Note) : IRequest<Result>;

/// <summary>Withdraws one's own request before anyone has decided.</summary>
/// <param name="Id">The pending request.</param>
public sealed record WithdrawCriticalActionCommand(Guid Id) : IRequest<Result>;

/// <summary>
/// Approves a request and executes it in the same unit of work.
///
/// The domain refuses an approval from the person who raised the request, so the two-person rule
/// cannot be bypassed by calling this directly. Execution runs before the save: if the underlying
/// action fails, nothing is committed and the request stays pending rather than being recorded as
/// approved with nothing done.
/// </summary>
public sealed class ApproveCriticalActionCommandHandler : IRequestHandler<ApproveCriticalActionCommand, Result>
{
    private readonly ICriticalActionRepository _actions;
    private readonly IEnumerable<ICriticalActionExecutor> _executors;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public ApproveCriticalActionCommandHandler(
        ICriticalActionRepository actions,
        IEnumerable<ICriticalActionExecutor> executors,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _actions = actions;
        _executors = executors;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(ApproveCriticalActionCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure(Error.Unauthorized("criticalAction.unauthorized", "Authentication is required."));

        var action = await _actions.GetByIdAsync(request.Id, ct);
        if (action is null)
            return Result.Failure(Error.NotFound("criticalAction.notFound", "Request not found."));

        var now = _clock.UtcNow;

        var executor = _executors.FirstOrDefault(e => e.Kind == action.Kind);
        if (executor is null)
            return Result.Failure(Error.Conflict(
                "criticalAction.notExecutable", $"No executor is wired for {action.Kind} yet."));

        try
        {
            action.Approve(userId.Value, now);
        }
        catch (DomainException ex)
        {
            // Self-approval, an already-decided request, a closed window: all of them are the caller
            // asking for something the record does not allow, not a server fault.
            return Result.Failure(Error.Conflict("criticalAction.notApprovable", ex.Message));
        }

        var executed = await executor.ExecuteAsync(action, ct);
        if (executed.IsFailure)
            return executed;

        _actions.Update(action);

        await _audit.WriteAsync(
            AuditEntities.CriticalAction, action.Id, AuditEvents.CriticalActionApproved,
            $"Подтверждено вторым лицом: {action.Kind}",
            AuditSeverity.Success, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>Declines a request. Same two-person rule: the requester cannot decide their own request.</summary>
public sealed class RejectCriticalActionCommandHandler : IRequestHandler<RejectCriticalActionCommand, Result>
{
    private readonly ICriticalActionRepository _actions;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public RejectCriticalActionCommandHandler(
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

    public async Task<Result> Handle(RejectCriticalActionCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure(Error.Unauthorized("criticalAction.unauthorized", "Authentication is required."));

        if (string.IsNullOrWhiteSpace(request.Note))
            return Result.Failure(Error.Validation("criticalAction.noteRequired", "A reason is required."));

        var action = await _actions.GetByIdAsync(request.Id, ct);
        if (action is null)
            return Result.Failure(Error.NotFound("criticalAction.notFound", "Request not found."));

        try
        {
            action.Reject(userId.Value, request.Note, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("criticalAction.notDecidable", ex.Message));
        }

        _actions.Update(action);

        await _audit.WriteAsync(
            AuditEntities.CriticalAction, action.Id, AuditEvents.CriticalActionRejected,
            $"Отклонено вторым лицом: {action.Kind}. {request.Note}",
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>Withdraws one's own pending request.</summary>
public sealed class WithdrawCriticalActionCommandHandler : IRequestHandler<WithdrawCriticalActionCommand, Result>
{
    private readonly ICriticalActionRepository _actions;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public WithdrawCriticalActionCommandHandler(
        ICriticalActionRepository actions, ICurrentUserService currentUser,
        IDateTimeProvider clock, IUnitOfWork uow)
    {
        _actions = actions;
        _currentUser = currentUser;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(WithdrawCriticalActionCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure(Error.Unauthorized("criticalAction.unauthorized", "Authentication is required."));

        var action = await _actions.GetByIdAsync(request.Id, ct);
        if (action is null)
            return Result.Failure(Error.NotFound("criticalAction.notFound", "Request not found."));

        try
        {
            action.Withdraw(userId.Value, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("criticalAction.notWithdrawable", ex.Message));
        }

        _actions.Update(action);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
