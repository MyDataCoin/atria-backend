using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Operations.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Users;

namespace Atria.Application.Operations.Commands;

/// <summary>
/// Revises a draft period's figures. Admin or Finance.
/// </summary>
/// <param name="Id">The period.</param>
/// <param name="GrossRevenue">Corrected income.</param>
/// <param name="OperatingExpenses">Corrected costs.</param>
/// <param name="Note">Note; <c>null</c> leaves the existing one.</param>
/// <param name="Lines">Replaces the whole breakdown when supplied; <c>null</c> leaves it, <c>[]</c> clears it.</param>
public sealed record ReviseOperatingPeriodCommand(
    Guid Id,
    decimal GrossRevenue,
    decimal OperatingExpenses,
    string? Note,
    IReadOnlyList<OperatingPeriodLineInput>? Lines) : IRequest<Result>;

/// <summary>
/// Corrects the figures while they are still a draft. A confirmed period cannot be revised — a
/// distribution may already have been sized against it, and moving the ceiling under a run that has
/// been approved is how money goes out that the income never covered.
/// </summary>
public sealed class ReviseOperatingPeriodCommandHandler
    : IRequestHandler<ReviseOperatingPeriodCommand, Result>
{
    private readonly IOperatingPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public ReviseOperatingPeriodCommandHandler(
        IOperatingPeriodRepository periods,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _periods = periods;
        _currentUser = currentUser;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(ReviseOperatingPeriodCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(
                Error.Unauthorized("operatingPeriod.unauthorized", "Authentication is required."));
        if (!_currentUser.IsInRole(Role.Admin) && !_currentUser.IsInRole(Role.Finance))
            return Result.Failure(Error.Forbidden(
                "operatingPeriod.forbidden", "Only an administrator or finance can revise a period."));

        var period = await _periods.GetByIdAsync(request.Id, ct);
        if (period is null)
            return Result.Failure(Error.NotFound("operatingPeriod.notFound", "Operating period not found."));

        try
        {
            period.Revise(request.GrossRevenue, request.OperatingExpenses, request.Note);

            if (request.Lines is not null)
                period.ReplaceLines(request.Lines.Select(
                    l => (OperatingPeriodDto.ParseKind(l.Kind), l.Label, l.Amount)));
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("operatingPeriod.cannotRevise", ex.Message));
        }

        await _audit.WriteAsync(
            AuditEntities.Property, period.PropertyId, AuditEvents.OperatingPeriodRevised,
            $"Отчётный период {period.StartUtc:yyyy-MM-dd}—{period.EndUtc:yyyy-MM-dd} изменён: "
            + $"доход {period.GrossRevenue:0.##}, расходы {period.OperatingExpenses:0.##}, "
            + $"к распределению {period.NetIncome:0.##} {period.Currency}",
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>
/// Confirms a period's figures, freezing them so a distribution may be declared from them.
/// Admin or Finance.
/// </summary>
/// <param name="Id">The period.</param>
public sealed record ConfirmOperatingPeriodCommand(Guid Id) : IRequest<Result>;

/// <summary>
/// The owner's sign-off. The confirmer may not be whoever reported the figures — the management
/// company asked for the computation and the sign-off to be done by different people, and the
/// aggregate enforces it so the rule cannot be bypassed by calling a different endpoint.
/// </summary>
public sealed class ConfirmOperatingPeriodCommandHandler
    : IRequestHandler<ConfirmOperatingPeriodCommand, Result>
{
    private readonly IOperatingPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public ConfirmOperatingPeriodCommandHandler(
        IOperatingPeriodRepository periods,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _periods = periods;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(ConfirmOperatingPeriodCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure(
                Error.Unauthorized("operatingPeriod.unauthorized", "Authentication is required."));
        if (!_currentUser.IsInRole(Role.Admin) && !_currentUser.IsInRole(Role.Finance))
            return Result.Failure(Error.Forbidden(
                "operatingPeriod.forbidden", "Only an administrator or finance can confirm a period."));

        var period = await _periods.GetByIdAsync(request.Id, ct);
        if (period is null)
            return Result.Failure(Error.NotFound("operatingPeriod.notFound", "Operating period not found."));

        try
        {
            period.Confirm(userId.Value, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("operatingPeriod.cannotConfirm", ex.Message));
        }

        await _audit.WriteAsync(
            AuditEntities.Property, period.PropertyId, AuditEvents.OperatingPeriodConfirmed,
            $"Отчётный период {period.StartUtc:yyyy-MM-dd}—{period.EndUtc:yyyy-MM-dd} подтверждён. "
            + $"К распределению {period.NetIncome:0.##} {period.Currency}",
            AuditSeverity.Success, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
