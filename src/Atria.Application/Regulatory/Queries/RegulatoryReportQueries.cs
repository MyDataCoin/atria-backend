using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Regulatory;

namespace Atria.Application.Regulatory.Queries;

/// <summary>A filing obligation as the reminder list shows it.</summary>
/// <param name="Id">Obligation identifier.</param>
/// <param name="Kind">Which notification (serialized by name).</param>
/// <param name="PropertyId">The issue it concerns; null for platform-wide notifications.</param>
/// <param name="PeriodStartUtc">Start of the period covered.</param>
/// <param name="PeriodEndUtc">End of the period covered.</param>
/// <param name="DueAtUtc">When it must be filed.</param>
/// <param name="Status">Due, Generated or Filed (serialized by name).</param>
/// <param name="WorkingDaysLeft">Working days remaining before the deadline; 0 once it has passed.</param>
/// <param name="IsOverdue">Past the deadline and still not filed.</param>
/// <param name="GeneratedAtUtc">When the content was produced.</param>
/// <param name="FiledAtUtc">When it was filed.</param>
/// <param name="FilingReference">The regulator's acknowledgement reference.</param>
/// <param name="HolderSnapshotId">The frozen register the figures were drawn from, where one applies.</param>
public sealed record RegulatoryReportDto(
    Guid Id,
    RegulatoryReportKind Kind,
    Guid? PropertyId,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateTime DueAtUtc,
    RegulatoryReportStatus Status,
    int WorkingDaysLeft,
    bool IsOverdue,
    DateTime? GeneratedAtUtc,
    DateTime? FiledAtUtc,
    string? FilingReference,
    Guid? HolderSnapshotId);

/// <summary>The obligation with its generated content.</summary>
/// <param name="Report">The obligation.</param>
/// <param name="Content">The generated document as JSON; null until it is produced.</param>
public sealed record RegulatoryReportDetailDto(RegulatoryReportDto Report, string? Content);

/// <summary>
/// Obligations not yet filed, earliest deadline first — the reminder list. Deadlines run in working
/// days, so a notification due "in 5 days" over a weekend is nearer than it looks.
/// </summary>
public sealed record GetOutstandingReportsQuery : IRequest<Result<IReadOnlyList<RegulatoryReportDto>>>;

/// <summary>Every obligation, newest period first, optionally narrowed to one issue.</summary>
/// <param name="PropertyId">The issue to narrow to; null for all.</param>
/// <param name="Take">How many to return, capped at 200.</param>
public sealed record ListRegulatoryReportsQuery(Guid? PropertyId, int Take = 50)
    : IRequest<Result<IReadOnlyList<RegulatoryReportDto>>>;

/// <summary>One obligation with its generated content.</summary>
/// <param name="Id">Obligation identifier.</param>
public sealed record GetRegulatoryReportQuery(Guid Id) : IRequest<Result<RegulatoryReportDetailDto>>;

public sealed class GetOutstandingReportsQueryHandler
    : IRequestHandler<GetOutstandingReportsQuery, Result<IReadOnlyList<RegulatoryReportDto>>>
{
    private readonly IRegulatoryReportRepository _reports;
    private readonly IDateTimeProvider _clock;

    public GetOutstandingReportsQueryHandler(IRegulatoryReportRepository reports, IDateTimeProvider clock)
    {
        _reports = reports;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<RegulatoryReportDto>>> Handle(
        GetOutstandingReportsQuery request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        IReadOnlyList<RegulatoryReportDto> dtos = (await _reports.ListOutstandingAsync(ct))
            .Select(r => RegulatoryReportMapper.ToDto(r, now))
            .ToList();

        return Result.Success(dtos);
    }
}

public sealed class ListRegulatoryReportsQueryHandler
    : IRequestHandler<ListRegulatoryReportsQuery, Result<IReadOnlyList<RegulatoryReportDto>>>
{
    private readonly IRegulatoryReportRepository _reports;
    private readonly IDateTimeProvider _clock;

    public ListRegulatoryReportsQueryHandler(IRegulatoryReportRepository reports, IDateTimeProvider clock)
    {
        _reports = reports;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<RegulatoryReportDto>>> Handle(
        ListRegulatoryReportsQuery request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var take = Math.Clamp(request.Take, 1, 200);

        IReadOnlyList<RegulatoryReportDto> dtos = (await _reports.ListAsync(request.PropertyId, take, ct))
            .Select(r => RegulatoryReportMapper.ToDto(r, now))
            .ToList();

        return Result.Success(dtos);
    }
}

public sealed class GetRegulatoryReportQueryHandler
    : IRequestHandler<GetRegulatoryReportQuery, Result<RegulatoryReportDetailDto>>
{
    private readonly IRegulatoryReportRepository _reports;
    private readonly IDateTimeProvider _clock;

    public GetRegulatoryReportQueryHandler(IRegulatoryReportRepository reports, IDateTimeProvider clock)
    {
        _reports = reports;
        _clock = clock;
    }

    public async Task<Result<RegulatoryReportDetailDto>> Handle(
        GetRegulatoryReportQuery request, CancellationToken ct)
    {
        var report = await _reports.GetByIdAsync(request.Id, ct);
        if (report is null)
            return Result.Failure<RegulatoryReportDetailDto>(
                Error.NotFound("report.notFound", "Report not found."));

        return Result.Success(new RegulatoryReportDetailDto(
            RegulatoryReportMapper.ToDto(report, _clock.UtcNow), report.Content));
    }
}

internal static class RegulatoryReportMapper
{
    public static RegulatoryReportDto ToDto(RegulatoryReport r, DateTime nowUtc) => new(
        r.Id, r.Kind, r.PropertyId, r.PeriodStartUtc, r.PeriodEndUtc, r.DueAtUtc, r.Status,
        r.WorkingDaysLeft(nowUtc), r.IsOverdueAt(nowUtc), r.GeneratedAtUtc, r.FiledAtUtc,
        r.FilingReference, r.HolderSnapshotId);
}
