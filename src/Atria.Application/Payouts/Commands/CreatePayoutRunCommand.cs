using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Operations;
using Atria.Domain.Payouts;

namespace Atria.Application.Payouts.Commands;

/// <summary>
/// Computes a distribution to the holders of an issue against a frozen register.
/// </summary>
/// <param name="SnapshotId">The payout snapshot the distribution is divided across.</param>
/// <param name="Kind">Income distribution or return of capital.</param>
/// <param name="Method">How the money will reach holders — bank transfer or wallet.</param>
/// <param name="DeclaredAmount">The total being distributed.</param>
/// <param name="Currency">Currency of the declared amount.</param>
/// <param name="Note">What this distribution is, in the operator's words.</param>
/// <param name="OperatingPeriodId">
/// The confirmed period the money comes from. Required for a dividend — that is what makes the
/// declared amount traceable to income the owner signed off — and not accepted for a capital return,
/// which is not paid out of operating income at all.
/// </param>
public sealed record CreatePayoutRunCommand(
    Guid SnapshotId,
    PayoutKind Kind,
    PayoutMethod Method,
    decimal DeclaredAmount,
    string Currency,
    string? Note,
    Guid? OperatingPeriodId = null) : IRequest<Result<Guid>>;

/// <summary>
/// Draws up a distribution. It is created as a draft that pays nobody: money only moves after a
/// second person approves it through the dual-approval path.
/// </summary>
/// <remarks>
/// One distribution per cut, checked here and enforced by a unique index. Two runs on the same
/// snapshot would divide the same register twice and pay every holder double, and that is the kind
/// of mistake that is discovered only after the transfers have gone out.
/// <para>
/// A dividend must name the confirmed operating period it comes from, and cannot exceed that
/// period's net income. Before this, the declared amount was simply a number somebody typed — the
/// division across the register was exact, but "where did this figure come from?" had no answer in
/// the system. Now it does, and the period is marked distributed so the same income cannot be paid
/// out twice.
/// </para>
/// </remarks>
public sealed class CreatePayoutRunCommandHandler
    : IRequestHandler<CreatePayoutRunCommand, Result<Guid>>
{
    private readonly IPayoutRunRepository _runs;
    private readonly IHolderSnapshotRepository _snapshots;
    private readonly IPropertyRepository _properties;
    private readonly IOperatingPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public CreatePayoutRunCommandHandler(
        IPayoutRunRepository runs,
        IHolderSnapshotRepository snapshots,
        IPropertyRepository properties,
        IOperatingPeriodRepository periods,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _runs = runs;
        _snapshots = snapshots;
        _properties = properties;
        _periods = periods;
        _currentUser = currentUser;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result<Guid>> Handle(CreatePayoutRunCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<Guid>(
                Error.Unauthorized("payout.unauthorized", "Authentication is required."));

        var snapshot = await _snapshots.GetWithRowsAsync(request.SnapshotId, ct);
        if (snapshot is null)
            return Result.Failure<Guid>(
                Error.NotFound("payout.snapshotNotFound", "Holder snapshot not found."));

        var existing = await _runs.FindBySnapshotAsync(snapshot.Id, ct);
        if (existing is not null)
            return Result.Failure<Guid>(Error.Conflict(
                "payout.alreadyComputed",
                "A distribution has already been computed against this cut of the register."));

        // A dividend is money the object earned, so it has to point at the period it earned it in.
        // A capital return is not — it is the issue being wound up — and attaching one to a period
        // would consume that period's income without distributing it.
        OperatingPeriod? period = null;
        if (request.Kind == PayoutKind.Dividend)
        {
            if (request.OperatingPeriodId is null)
                return Result.Failure<Guid>(Error.Validation(
                    "payout.periodRequired",
                    "A dividend must name the confirmed operating period it is declared from."));

            period = await _periods.GetByIdAsync(request.OperatingPeriodId.Value, ct);
            if (period is null)
                return Result.Failure<Guid>(Error.NotFound(
                    "payout.periodNotFound", "Operating period not found."));

            if (period.PropertyId != snapshot.PropertyId)
                return Result.Failure<Guid>(Error.Validation(
                    "payout.periodMismatch",
                    "The operating period belongs to a different issue than the register being divided."));
        }
        else if (request.OperatingPeriodId is not null)
        {
            return Result.Failure<Guid>(Error.Validation(
                "payout.periodNotApplicable",
                "A capital return is not paid out of operating income and cannot name a period."));
        }

        PayoutRun run;
        try
        {
            run = PayoutRun.Create(
                snapshot, request.Kind, request.Method, request.DeclaredAmount, request.Currency,
                userId.Value, request.Note);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(Error.Validation("payout.invalid", ex.Message));
        }

        // Marked before the run is saved so the period's own rules — confirmed, not already
        // distributed, and the amount within its net income — decide whether this run exists at all.
        if (period is not null)
        {
            try
            {
                period.MarkDistributed(run.Id, run.DeclaredAmount);
            }
            catch (DomainException ex)
            {
                return Result.Failure<Guid>(Error.Conflict("payout.periodUnavailable", ex.Message));
            }

            _periods.Update(period);
        }

        await _runs.AddAsync(run, ct);

        var property = await _properties.GetByIdAsync(run.PropertyId, ct);

        await _audit.WriteAsync(
            AuditEntities.PayoutRun, run.Id, AuditEvents.PayoutRunCreated,
            $"Рассчитано распределение {run.DeclaredAmount} {run.Currency} по объекту "
            + $"«{property?.Name ?? run.PropertyId.ToString()}» на {run.Items.Count} держателей "
            + $"по реестру от {run.SnapshotAtUtc:u}",
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success(run.Id);
    }
}
