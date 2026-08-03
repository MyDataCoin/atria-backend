using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Governance;

namespace Atria.Application.Governance.Executors;

/// <summary>
/// Opens a distribution for settlement once a second person has approved it.
/// </summary>
/// <remarks>
/// This is the case dual approval exists for. Everything else it guards — blocking an account,
/// publishing an issue — is damage that can at least be argued about afterwards; a distribution
/// sends real money to real people, and once the transfers are out there is nothing to undo them
/// with. Until this executor runs, the run is arithmetic on a page: it names amounts but authorises
/// nothing.
/// </remarks>
public sealed class ApprovePayoutRunExecutor : ICriticalActionExecutor
{
    private readonly IPayoutRunRepository _runs;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;

    public ApprovePayoutRunExecutor(
        IPayoutRunRepository runs, IDateTimeProvider clock, IAuditWriter audit)
    {
        _runs = runs;
        _clock = clock;
        _audit = audit;
    }

    public CriticalActionKind Kind => CriticalActionKind.PayoutRun;

    public async Task<Result> ExecuteAsync(CriticalAction action, CancellationToken ct)
    {
        var run = await _runs.GetWithItemsAsync(action.TargetId, ct);
        if (run is null)
            return Result.Failure(Error.NotFound("payout.notFound", "Distribution not found."));

        try
        {
            run.Approve(_clock.UtcNow);
        }
        catch (DomainException ex)
        {
            // A run that is no longer a draft was cancelled or already approved while the request
            // sat waiting. Failing here leaves the request pending rather than recording an approval
            // of something that did not happen.
            return Result.Failure(Error.Conflict("payout.notApprovable", ex.Message));
        }

        _runs.Update(run);

        await _audit.WriteAsync(
            AuditEntities.PayoutRun, run.Id, AuditEvents.PayoutRunApproved,
            $"Распределение {run.DeclaredAmount} {run.Currency} на {run.Items.Count} держателей "
            + $"открыто к выплате по двум подтверждениям",
            AuditSeverity.Alert, ct);

        return Result.Success();
    }
}
