using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Investments.Services;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Investments;
using Atria.Domain.Refunds;

namespace Atria.Application.Investments.Commands;

/// <summary>Voids an application that should not stand and returns its shares to the issue. Operator only.</summary>
/// <param name="InvestmentId">The application to annul.</param>
/// <param name="Reason">Why it is being voided; required and journalled.</param>
/// <param name="RecordRefund">
/// Whether money reached us for this application and is therefore owed back. Only the operator knows:
/// settlement happens off the platform, so the platform cannot work it out. Left on by default —
/// recording a debt that turns out not to exist is a line someone deletes, while missing one is money
/// an investor never sees again.
/// </param>
public sealed record AnnulInvestmentCommand(Guid InvestmentId, string Reason, bool RecordRefund = true)
    : IRequest<Result>;

/// <summary>
/// Unwinds an application the platform should not be carrying — a mistaken entry, a duplicate, or data
/// left over from testing.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that removing such a row is never done by hand in the database. A <c>DELETE</c> there
/// looks like it worked and quietly takes the shares with it: the reservation already decremented the
/// issue's available supply, and nothing puts it back. The issue then offers fewer shares than it has,
/// permanently, with no record of why.
/// </para>
/// <para>
/// The effects mirror a withdrawal, because an application that had been activated placed shares that
/// have to be unplaced: the shares go back on sale, the register stops counting them, anything minted
/// is burned, and — when money had actually been received — the refund is recorded. An application that
/// never activated placed nothing, so only the reservation is released.
/// </para>
/// </remarks>
public sealed class AnnulInvestmentCommandHandler : IRequestHandler<AnnulInvestmentCommand, Result>
{
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;
    private readonly InvestmentUnwinder _unwinder;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public AnnulInvestmentCommandHandler(
        IInvestmentRepository investments,
        IPropertyRepository properties,
        InvestmentUnwinder unwinder,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _investments = investments;
        _properties = properties;
        _unwinder = unwinder;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(AnnulInvestmentCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure(Error.Validation("investment.reasonRequired", "An annulment reason is required."));

        var investment = await _investments.GetByIdAsync(request.InvestmentId, ct);
        if (investment is null)
            return Result.Failure(Error.NotFound("investment.notFound", "Investment not found."));

        // Read before the transition: afterwards the status is Annulled and no longer says whether
        // anything had actually been placed.
        var wasActive = investment.Status == InvestmentStatus.Active;

        try
        {
            investment.Annul(request.Reason);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("investment.notAnnullable", ex.Message));
        }

        _investments.Update(investment);

        var property = await _properties.GetByIdAsync(investment.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("investment.propertyNotFound", "Property not found."));

        // The whole point: the shares go back on sale. Reserved or Active, they were held out of the
        // pool either way.
        property.ReleaseTokens(investment.TokenCount);
        _properties.Update(property);

        await _unwinder.UnwindAsync(
            property, investment, wasActive,
            $"Заявка {investment.Id} аннулирована оператором: {request.Reason}",
            RefundReason.IssueAnnulled, "annulment", "AnnulledByOperator",
            request.RecordRefund, ct);

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.InvestmentAnnulled,
            $"Заявка на {investment.TokenCount} долей объекта «{property.Name}» аннулирована оператором: "
            + request.Reason,
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
