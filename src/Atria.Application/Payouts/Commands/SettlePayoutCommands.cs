using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Payouts;

namespace Atria.Application.Payouts.Commands;

/// <summary>Records that one holder was paid, with the reference proving it.</summary>
/// <param name="RunId">The distribution.</param>
/// <param name="ItemId">The holder's line.</param>
/// <param name="SettlementReference">Bank payment order number, or the transaction hash.</param>
public sealed record SettlePayoutItemCommand(Guid RunId, Guid ItemId, string SettlementReference)
    : IRequest<Result>;

/// <summary>Records that one holder's payment could not be delivered.</summary>
/// <param name="RunId">The distribution.</param>
/// <param name="ItemId">The holder's line.</param>
/// <param name="Reason">What went wrong, so somebody can fix it and try again.</param>
public sealed record FailPayoutItemCommand(Guid RunId, Guid ItemId, string Reason) : IRequest<Result>;

/// <summary>Closes a distribution once every line has an outcome.</summary>
public sealed record CompletePayoutRunCommand(Guid RunId) : IRequest<Result>;

/// <summary>Calls off a distribution before any money has moved.</summary>
public sealed record CancelPayoutRunCommand(Guid RunId, string Reason) : IRequest<Result>;

/// <summary>
/// The settlement side of a distribution: recording what actually happened to each holder's money.
/// </summary>
/// <remarks>
/// The platform does not move funds, so these commands are how the outside world reports back. Every
/// one of them writes to the audit journal, because "was this holder paid, and on what evidence" is
/// precisely the question a distribution has to answer years later.
/// </remarks>
public sealed class PayoutSettlementHandlers
    : IRequestHandler<SettlePayoutItemCommand, Result>,
      IRequestHandler<FailPayoutItemCommand, Result>,
      IRequestHandler<CompletePayoutRunCommand, Result>,
      IRequestHandler<CancelPayoutRunCommand, Result>
{
    private readonly IPayoutRunRepository _runs;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public PayoutSettlementHandlers(
        IPayoutRunRepository runs, IDateTimeProvider clock, IAuditWriter audit, IUnitOfWork uow)
    {
        _runs = runs;
        _clock = clock;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(SettlePayoutItemCommand request, CancellationToken ct)
        => await MutateAsync(
            request.RunId,
            run =>
            {
                run.SettleItem(request.ItemId, request.SettlementReference, _clock.UtcNow);
                var item = run.Items.First(i => i.Id == request.ItemId);
                return (AuditEvents.PayoutItemSettled, AuditSeverity.Success,
                    $"Выплачено {item.Amount} {run.Currency} держателю {item.WalletAddress}; "
                    + $"основание платежа {request.SettlementReference}");
            },
            ct);

    public async Task<Result> Handle(FailPayoutItemCommand request, CancellationToken ct)
        => await MutateAsync(
            request.RunId,
            run =>
            {
                run.FailItem(request.ItemId, request.Reason);
                var item = run.Items.First(i => i.Id == request.ItemId);
                return (AuditEvents.PayoutItemFailed, AuditSeverity.Alert,
                    $"Не доставлена выплата {item.Amount} {run.Currency} держателю "
                    + $"{item.WalletAddress}: {request.Reason}. Сумма остаётся к выплате");
            },
            ct);

    public async Task<Result> Handle(CompletePayoutRunCommand request, CancellationToken ct)
        => await MutateAsync(
            request.RunId,
            run =>
            {
                run.Complete(_clock.UtcNow);
                return (AuditEvents.PayoutRunCompleted, AuditSeverity.Success,
                    $"Распределение закрыто: выплачено {run.PaidAmount} из {run.DeclaredAmount} "
                    + $"{run.Currency}");
            },
            ct);

    public async Task<Result> Handle(CancelPayoutRunCommand request, CancellationToken ct)
        => await MutateAsync(
            request.RunId,
            run =>
            {
                run.Cancel(request.Reason);
                return (AuditEvents.PayoutRunCancelled, AuditSeverity.Warning,
                    $"Распределение {run.DeclaredAmount} {run.Currency} отменено: {request.Reason}");
            },
            ct);

    /// <summary>
    /// Loads the run with its lines, applies the change, and journals it. The aggregate holds every
    /// rule about what may happen in which state, so a refusal here is a conflict, not a validation
    /// failure — the caller asked for something the distribution's current state forbids.
    /// </summary>
    private async Task<Result> MutateAsync(
        Guid runId, Func<PayoutRun, (string Event, AuditSeverity Severity, string Summary)> change,
        CancellationToken ct)
    {
        var run = await _runs.GetWithItemsAsync(runId, ct);
        if (run is null)
            return Result.Failure(Error.NotFound("payout.notFound", "Distribution not found."));

        string auditEvent;
        AuditSeverity severity;
        string summary;
        try
        {
            (auditEvent, severity, summary) = change(run);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("payout.conflict", ex.Message));
        }

        _runs.Update(run);
        await _audit.WriteAsync(AuditEntities.PayoutRun, run.Id, auditEvent, summary, severity, ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
