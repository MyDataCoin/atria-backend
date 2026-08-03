using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Governance;

namespace Atria.Application.Governance.Queries;

/// <summary>A dual-approval request as the approval queue shows it.</summary>
/// <param name="Id">Request identifier.</param>
/// <param name="Kind">What is being asked for (serialized by name).</param>
/// <param name="TargetId">The entity the action applies to.</param>
/// <param name="Reason">Why the requester is asking.</param>
/// <param name="RequestedByUserId">Who asked — never the same person who may approve.</param>
/// <param name="RequestedAtUtc">When it was raised.</param>
/// <param name="ExpiresAtUtc">After this instant it can no longer be approved.</param>
/// <param name="Status">Current state of the request (serialized by name).</param>
/// <param name="DecidedByUserId">Who decided, once someone has.</param>
/// <param name="DecidedAtUtc">When it was decided.</param>
/// <param name="DecisionNote">The decider's note; always present on a refusal.</param>
public sealed record CriticalActionDto(
    Guid Id,
    CriticalActionKind Kind,
    Guid TargetId,
    string? Reason,
    Guid RequestedByUserId,
    DateTime RequestedAtUtc,
    DateTime ExpiresAtUtc,
    CriticalActionStatus Status,
    Guid? DecidedByUserId,
    DateTime? DecidedAtUtc,
    string? DecisionNote);

/// <summary>Requests awaiting a decision, oldest first.</summary>
public sealed record GetPendingCriticalActionsQuery : IRequest<Result<IReadOnlyList<CriticalActionDto>>>;

/// <summary>Recently decided requests, newest first — who approved what, and who refused.</summary>
/// <param name="Take">How many to return, capped at 200.</param>
public sealed record GetDecidedCriticalActionsQuery(int Take = 50)
    : IRequest<Result<IReadOnlyList<CriticalActionDto>>>;

public sealed class GetPendingCriticalActionsQueryHandler
    : IRequestHandler<GetPendingCriticalActionsQuery, Result<IReadOnlyList<CriticalActionDto>>>
{
    private readonly ICriticalActionRepository _actions;

    public GetPendingCriticalActionsQueryHandler(ICriticalActionRepository actions) => _actions = actions;

    public async Task<Result<IReadOnlyList<CriticalActionDto>>> Handle(
        GetPendingCriticalActionsQuery request, CancellationToken ct)
    {
        var actions = await _actions.ListPendingAsync(ct);
        IReadOnlyList<CriticalActionDto> dtos = actions.Select(CriticalActionMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

public sealed class GetDecidedCriticalActionsQueryHandler
    : IRequestHandler<GetDecidedCriticalActionsQuery, Result<IReadOnlyList<CriticalActionDto>>>
{
    private readonly ICriticalActionRepository _actions;

    public GetDecidedCriticalActionsQueryHandler(ICriticalActionRepository actions) => _actions = actions;

    public async Task<Result<IReadOnlyList<CriticalActionDto>>> Handle(
        GetDecidedCriticalActionsQuery request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 200);
        var actions = await _actions.ListDecidedAsync(take, ct);
        IReadOnlyList<CriticalActionDto> dtos = actions.Select(CriticalActionMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

internal static class CriticalActionMapper
{
    public static CriticalActionDto ToDto(CriticalAction a) => new(
        a.Id, a.Kind, a.TargetId, a.Reason, a.RequestedByUserId, a.RequestedAtUtc, a.ExpiresAtUtc,
        a.Status, a.DecidedByUserId, a.DecidedAtUtc, a.DecisionNote);
}
