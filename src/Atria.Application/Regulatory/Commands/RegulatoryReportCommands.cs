using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Regulatory;

namespace Atria.Application.Regulatory.Commands;

/// <summary>Records a filing obligation and its deadline. Idempotent per (kind, issue, period).</summary>
/// <param name="Kind">Which notification.</param>
/// <param name="PropertyId">The issue it concerns; null for platform-wide notifications.</param>
/// <param name="PeriodStartUtc">Start of the period covered.</param>
/// <param name="PeriodEndUtc">End of the period covered — the deadline counts from here.</param>
public sealed record RaiseRegulatoryReportCommand(
    RegulatoryReportKind Kind, Guid? PropertyId, DateTime PeriodStartUtc, DateTime PeriodEndUtc)
    : IRequest<Result<Guid>>;

/// <summary>Generates the content of a recorded obligation from system data.</summary>
/// <param name="Id">The obligation.</param>
public sealed record GenerateRegulatoryReportCommand(Guid Id) : IRequest<Result>;

/// <summary>Marks a generated notification as filed with the regulator.</summary>
/// <param name="Id">The obligation.</param>
/// <param name="FilingReference">The regulator's acknowledgement reference.</param>
public sealed record MarkRegulatoryReportFiledCommand(Guid Id, string FilingReference) : IRequest<Result>;

public sealed class RaiseRegulatoryReportCommandHandler
    : IRequestHandler<RaiseRegulatoryReportCommand, Result<Guid>>
{
    private readonly IRegulatoryReportRepository _reports;
    private readonly IUnitOfWork _uow;

    public RaiseRegulatoryReportCommandHandler(IRegulatoryReportRepository reports, IUnitOfWork uow)
    {
        _reports = reports;
        _uow = uow;
    }

    public async Task<Result<Guid>> Handle(RaiseRegulatoryReportCommand request, CancellationToken ct)
    {
        // Raising the same period twice must not create a second deadline for the same obligation.
        var existing = await _reports.FindAsync(request.Kind, request.PropertyId, request.PeriodEndUtc, ct);
        if (existing is not null)
            return Result.Success(existing.Id);

        RegulatoryReport report;
        try
        {
            report = RegulatoryReport.Raise(
                request.Kind, request.PropertyId, request.PeriodStartUtc, request.PeriodEndUtc);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(Error.Validation("report.invalidPeriod", ex.Message));
        }

        await _reports.AddAsync(report, ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success(report.Id);
    }
}

/// <summary>
/// Produces the document from platform data. Regenerating before filing is deliberate: figures move
/// until the moment the notification is handed over, and a stale draft is worse than a fresh one.
/// </summary>
public sealed class GenerateRegulatoryReportCommandHandler
    : IRequestHandler<GenerateRegulatoryReportCommand, Result>
{
    private readonly IRegulatoryReportRepository _reports;
    private readonly IEnumerable<IRegulatoryReportComposer> _composers;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public GenerateRegulatoryReportCommandHandler(
        IRegulatoryReportRepository reports,
        IEnumerable<IRegulatoryReportComposer> composers,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _reports = reports;
        _composers = composers;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(GenerateRegulatoryReportCommand request, CancellationToken ct)
    {
        var report = await _reports.GetByIdAsync(request.Id, ct);
        if (report is null)
            return Result.Failure(Error.NotFound("report.notFound", "Report not found."));

        var composer = _composers.FirstOrDefault(c => c.Kind == report.Kind);
        if (composer is null)
            return Result.Failure(Error.Conflict(
                "report.noComposer", $"No composer is wired for {report.Kind}."));

        var composed = await composer.ComposeAsync(report, ct);
        if (composed.IsFailure)
            return Result.Failure(composed.Error);

        try
        {
            report.AttachContent(composed.Value.ContentJson, composed.Value.HolderSnapshotId, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("report.notGeneratable", ex.Message));
        }

        _reports.Update(report);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public sealed class MarkRegulatoryReportFiledCommandHandler
    : IRequestHandler<MarkRegulatoryReportFiledCommand, Result>
{
    private readonly IRegulatoryReportRepository _reports;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public MarkRegulatoryReportFiledCommandHandler(
        IRegulatoryReportRepository reports, IDateTimeProvider clock, IAuditWriter audit, IUnitOfWork uow)
    {
        _reports = reports;
        _clock = clock;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(MarkRegulatoryReportFiledCommand request, CancellationToken ct)
    {
        var report = await _reports.GetByIdAsync(request.Id, ct);
        if (report is null)
            return Result.Failure(Error.NotFound("report.notFound", "Report not found."));

        var now = _clock.UtcNow;
        var wasLate = report.IsOverdueAt(now);

        try
        {
            report.MarkFiled(request.FilingReference, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("report.notFilable", ex.Message));
        }

        _reports.Update(report);

        await _audit.WriteAsync(
            AuditEntities.RegulatoryReport, report.Id, AuditEvents.RegulatoryReportFiled,
            $"Уведомление регулятору подано: {report.Kind}, срок {report.DueAtUtc:yyyy-MM-dd}"
            + (wasLate ? " (с нарушением срока)" : string.Empty),
            wasLate ? AuditSeverity.Alert : AuditSeverity.Success, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
