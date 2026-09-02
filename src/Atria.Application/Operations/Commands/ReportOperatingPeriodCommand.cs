using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Operations.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Operations;
using Atria.Domain.Users;

namespace Atria.Application.Operations.Commands;

/// <summary>
/// Reports what an issue earned and spent over one stretch of time. Admin or Finance.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="StartUtc">First day of the period, inclusive.</param>
/// <param name="EndUtc">Last day of the period, inclusive.</param>
/// <param name="GrossRevenue">Rent and other income collected.</param>
/// <param name="OperatingExpenses">Costs incurred.</param>
/// <param name="Note">What the period covers, in the reporter's words.</param>
/// <param name="Lines">Optional breakdown behind the two totals.</param>
public sealed record ReportOperatingPeriodCommand(
    Guid PropertyId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal GrossRevenue,
    decimal OperatingExpenses,
    string? Note,
    IReadOnlyList<OperatingPeriodLineInput>? Lines) : IRequest<Result<Guid>>;

/// <summary>
/// Records the period as a draft. It pays nobody and blocks nothing until someone else confirms it —
/// the separation the management company asked for, where the person who computes the figures is not
/// the person who signs them off.
/// </summary>
public sealed class ReportOperatingPeriodCommandHandler
    : IRequestHandler<ReportOperatingPeriodCommand, Result<Guid>>
{
    private readonly IOperatingPeriodRepository _periods;
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public ReportOperatingPeriodCommandHandler(
        IOperatingPeriodRepository periods,
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _periods = periods;
        _properties = properties;
        _currentUser = currentUser;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result<Guid>> Handle(ReportOperatingPeriodCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<Guid>(
                Error.Unauthorized("operatingPeriod.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin) && !_currentUser.IsInRole(Role.Finance))
            return Result.Failure<Guid>(Error.Forbidden(
                "operatingPeriod.forbidden", "Only an administrator or finance can report a period."));

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<Guid>(Error.NotFound("property.notFound", "Property not found."));

        // Refused before the unique index refuses it, so the caller gets an explanation rather than a
        // database error. The index is still what actually guarantees it under concurrency.
        var existing = await _periods.FindByRangeAsync(
            request.PropertyId, request.StartUtc, request.EndUtc, ct);
        if (existing is not null)
            return Result.Failure<Guid>(Error.Conflict(
                "operatingPeriod.alreadyReported",
                "A period covering exactly these dates has already been reported for this issue."));

        OperatingPeriod period;
        try
        {
            period = OperatingPeriod.Report(
                request.PropertyId, request.StartUtc, request.EndUtc, request.GrossRevenue,
                request.OperatingExpenses, property.Currency, userId.Value, request.Note);

            if (request.Lines is { Count: > 0 })
                period.ReplaceLines(request.Lines.Select(
                    l => (OperatingPeriodDto.ParseKind(l.Kind), l.Label, l.Amount)));
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(Error.Validation("operatingPeriod.invalid", ex.Message));
        }

        await _periods.AddAsync(period, ct);

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.OperatingPeriodReported,
            $"Отчётный период {period.StartUtc:yyyy-MM-dd}—{period.EndUtc:yyyy-MM-dd} по объекту "
            + $"«{property.Name}»: доход {period.GrossRevenue:0.##}, расходы {period.OperatingExpenses:0.##}, "
            + $"к распределению {period.NetIncome:0.##} {period.Currency}",
            AuditSeverity.Success, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success(period.Id);
    }
}
