using System.Globalization;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Holders.Dtos;
using Atria.Application.Operations.Dtos;
using Atria.Domain.Operations;
using Atria.Domain.Users;

namespace Atria.Application.Operations.Queries;

/// <summary>An issue's operating periods, most recent first. Admin, Finance or Auditor.</summary>
/// <param name="PropertyId">The issue.</param>
public sealed record ListOperatingPeriodsQuery(Guid PropertyId)
    : IRequest<Result<IReadOnlyList<OperatingPeriodDto>>>;

public sealed class ListOperatingPeriodsQueryHandler
    : IRequestHandler<ListOperatingPeriodsQuery, Result<IReadOnlyList<OperatingPeriodDto>>>
{
    private readonly IOperatingPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;

    public ListOperatingPeriodsQueryHandler(
        IOperatingPeriodRepository periods, ICurrentUserService currentUser)
    {
        _periods = periods;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<OperatingPeriodDto>>> Handle(
        ListOperatingPeriodsQuery request, CancellationToken ct)
    {
        if (!CanRead(_currentUser))
            return Result.Failure<IReadOnlyList<OperatingPeriodDto>>(Error.Forbidden(
                "operatingPeriod.forbidden", "Not permitted to read operating periods."));

        var periods = await _periods.ListByPropertyAsync(request.PropertyId, ct);
        return Result.Success<IReadOnlyList<OperatingPeriodDto>>(
            periods.Select(OperatingPeriodDto.From).ToList());
    }

    internal static bool CanRead(ICurrentUserService user)
        => user.IsAuthenticated
           && (user.IsInRole(Role.Admin) || user.IsInRole(Role.Finance) || user.IsInRole(Role.Auditor));
}

/// <summary>One operating period with its breakdown. Admin, Finance or Auditor.</summary>
/// <param name="Id">The period.</param>
public sealed record GetOperatingPeriodQuery(Guid Id) : IRequest<Result<OperatingPeriodDto>>;

public sealed class GetOperatingPeriodQueryHandler
    : IRequestHandler<GetOperatingPeriodQuery, Result<OperatingPeriodDto>>
{
    private readonly IOperatingPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;

    public GetOperatingPeriodQueryHandler(
        IOperatingPeriodRepository periods, ICurrentUserService currentUser)
    {
        _periods = periods;
        _currentUser = currentUser;
    }

    public async Task<Result<OperatingPeriodDto>> Handle(
        GetOperatingPeriodQuery request, CancellationToken ct)
    {
        if (!ListOperatingPeriodsQueryHandler.CanRead(_currentUser))
            return Result.Failure<OperatingPeriodDto>(Error.Forbidden(
                "operatingPeriod.forbidden", "Not permitted to read operating periods."));

        var period = await _periods.GetByIdAsync(request.Id, ct);
        return period is null
            ? Result.Failure<OperatingPeriodDto>(
                Error.NotFound("operatingPeriod.notFound", "Operating period not found."))
            : Result.Success(OperatingPeriodDto.From(period));
    }
}

/// <summary>An issue's operating periods rendered as CSV for download. Admin, Finance or Auditor.</summary>
/// <param name="PropertyId">The issue.</param>
public sealed record ExportOperatingPeriodsQuery(Guid PropertyId)
    : IRequest<Result<HolderSnapshotFileDto>>;

/// <summary>
/// Renders an issue's periods as CSV — the "выгрузка из системы" half of what the management company
/// asked for, alongside the form that enters them.
/// </summary>
/// <remarks>
/// Generated on the server and deterministic: rows come out oldest period first, numbers are written
/// with an invariant format, and nothing about the moment of export enters the file, so exporting
/// twice yields identical bytes.
/// </remarks>
public sealed class ExportOperatingPeriodsQueryHandler
    : IRequestHandler<ExportOperatingPeriodsQuery, Result<HolderSnapshotFileDto>>
{
    private readonly IOperatingPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;

    public ExportOperatingPeriodsQueryHandler(
        IOperatingPeriodRepository periods, ICurrentUserService currentUser)
    {
        _periods = periods;
        _currentUser = currentUser;
    }

    public async Task<Result<HolderSnapshotFileDto>> Handle(
        ExportOperatingPeriodsQuery request, CancellationToken ct)
    {
        if (!ListOperatingPeriodsQueryHandler.CanRead(_currentUser))
            return Result.Failure<HolderSnapshotFileDto>(Error.Forbidden(
                "operatingPeriod.forbidden", "Not permitted to read operating periods."));

        var periods = await _periods.ListByPropertyAsync(request.PropertyId, ct);

        var csv = new StringBuilder();
        csv.Append("period_start,period_end,gross_revenue,operating_expenses,net_income,currency,status\n");

        foreach (var period in periods.OrderBy(p => p.StartUtc).ThenBy(p => p.EndUtc))
        {
            csv.Append(period.StartUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(period.EndUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(period.GrossRevenue.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(period.OperatingExpenses.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(period.NetIncome.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(period.Currency).Append(',')
                .Append(OperatingPeriodDto.ToWireStatus(period.Status)).Append('\n');
        }

        var fileName = $"operating-periods-{request.PropertyId}.csv";

        // UTF-8 with a BOM: Excel otherwise reads the file as the local ANSI code page.
        var content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());

        return Result.Success(new HolderSnapshotFileDto(fileName, "text/csv; charset=utf-8", content));
    }
}
