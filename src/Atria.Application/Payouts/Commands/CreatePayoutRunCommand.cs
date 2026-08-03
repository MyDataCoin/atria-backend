using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
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
public sealed record CreatePayoutRunCommand(
    Guid SnapshotId,
    PayoutKind Kind,
    PayoutMethod Method,
    decimal DeclaredAmount,
    string Currency,
    string? Note) : IRequest<Result<Guid>>;

/// <summary>
/// Draws up a distribution. It is created as a draft that pays nobody: money only moves after a
/// second person approves it through the dual-approval path.
/// </summary>
/// <remarks>
/// One distribution per cut, checked here and enforced by a unique index. Two runs on the same
/// snapshot would divide the same register twice and pay every holder double, and that is the kind
/// of mistake that is discovered only after the transfers have gone out.
/// </remarks>
public sealed class CreatePayoutRunCommandHandler
    : IRequestHandler<CreatePayoutRunCommand, Result<Guid>>
{
    private readonly IPayoutRunRepository _runs;
    private readonly IHolderSnapshotRepository _snapshots;
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public CreatePayoutRunCommandHandler(
        IPayoutRunRepository runs,
        IHolderSnapshotRepository snapshots,
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _runs = runs;
        _snapshots = snapshots;
        _properties = properties;
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
