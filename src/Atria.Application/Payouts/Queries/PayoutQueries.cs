using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Payouts.Dtos;
using Atria.Domain.Payouts;

namespace Atria.Application.Payouts.Queries;

/// <summary>Distributions on one issue, newest cut first.</summary>
public sealed record GetPayoutRunsQuery(Guid PropertyId) : IRequest<Result<IReadOnlyList<PayoutRunDto>>>;

/// <summary>One distribution with every holder line — the settlement working view.</summary>
public sealed record GetPayoutRunQuery(Guid RunId) : IRequest<Result<PayoutRunDetailDto>>;

/// <summary>Every distribution the current investor has a line in.</summary>
public sealed record GetMyPayoutsQuery : IRequest<Result<IReadOnlyList<MyPayoutDto>>>;

/// <summary>The operator-facing reads over distributions.</summary>
public sealed class PayoutQueryHandlers
    : IRequestHandler<GetPayoutRunsQuery, Result<IReadOnlyList<PayoutRunDto>>>,
      IRequestHandler<GetPayoutRunQuery, Result<PayoutRunDetailDto>>
{
    private readonly IPayoutRunRepository _runs;

    public PayoutQueryHandlers(IPayoutRunRepository runs) => _runs = runs;

    public async Task<Result<IReadOnlyList<PayoutRunDto>>> Handle(
        GetPayoutRunsQuery request, CancellationToken ct)
    {
        var runs = await _runs.ListByPropertyAsync(request.PropertyId, ct);
        return Result.Success<IReadOnlyList<PayoutRunDto>>(runs.Select(r => r.ToDto()).ToList());
    }

    public async Task<Result<PayoutRunDetailDto>> Handle(GetPayoutRunQuery request, CancellationToken ct)
    {
        var run = await _runs.GetWithItemsAsync(request.RunId, ct);
        if (run is null)
            return Result.Failure<PayoutRunDetailDto>(
                Error.NotFound("payout.notFound", "Distribution not found."));

        // Largest amounts first: that is the order the money is worked through and checked in.
        var items = run.Items
            .OrderByDescending(i => i.Amount)
            .ThenBy(i => i.WalletAddress, StringComparer.Ordinal)
            .Select(i => i.ToDto())
            .ToList();

        return Result.Success(new PayoutRunDetailDto(run.ToDto(), items));
    }
}

/// <summary>
/// The holder's own payout history.
/// </summary>
/// <remarks>
/// Scoped to the caller rather than filtered by a parameter: a distribution line names an amount, an
/// address and a person, and there is no reason for one investor to be able to ask about another's.
/// </remarks>
public sealed class GetMyPayoutsQueryHandler
    : IRequestHandler<GetMyPayoutsQuery, Result<IReadOnlyList<MyPayoutDto>>>
{
    private readonly IPayoutRunRepository _runs;
    private readonly ICurrentUserService _currentUser;

    public GetMyPayoutsQueryHandler(IPayoutRunRepository runs, ICurrentUserService currentUser)
    {
        _runs = runs;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<MyPayoutDto>>> Handle(
        GetMyPayoutsQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<IReadOnlyList<MyPayoutDto>>(
                Error.Unauthorized("payout.unauthorized", "Authentication is required."));

        var rows = await _runs.ListForInvestorAsync(userId.Value, ct);

        // A draft distribution is not shown: it has been approved by nobody and may still be called
        // off, and telling a holder they are owed money that no one has authorised is a promise the
        // platform has not made.
        var visible = rows
            .Where(r => r.Run.Status != PayoutRunStatus.Draft
                        && r.Run.Status != PayoutRunStatus.Cancelled)
            .Select(r => new MyPayoutDto(
                r.Run.Id, r.Run.PropertyId, r.Run.Kind, r.Run.Method, r.Run.SnapshotAtUtc,
                r.Item.TokenCount, r.Item.Amount, r.Run.Currency, r.Item.Status,
                r.Item.SettlementReference, r.Item.PaidAtUtc))
            .ToList();

        return Result.Success<IReadOnlyList<MyPayoutDto>>(visible);
    }
}
