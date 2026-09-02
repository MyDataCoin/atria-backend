using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Investments.Services;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Investments;
using Atria.Domain.Refunds;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Declares a placement unsubscribed and unwinds it: every application is voided, every share goes
/// back, and everyone who had been placed is owed their money. Admin only.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="Reason">Why the placement is being unwound. Required and journalled.</param>
public sealed record ClosePlacementUnsubscribedCommand(
    Guid PropertyId,
    string Reason) : IRequest<Result>;

/// <summary>
/// The other half of what the management company chose for an undersubscribed placement ("возврат и
/// продление") — the half that returns the money. <see cref="ExtendPlacementCommand"/> is the half
/// that keeps selling, and which of the two runs is a human decision, never automatic: the sweep
/// closes a placement that ran out of time but never unwinds one.
/// </summary>
/// <remarks>
/// <para>
/// Everything happens in one unit of work. A partially unwound placement — some investors refunded,
/// others still holding shares in an offering that no longer exists — is the one outcome that must
/// not be reachable, so the applications, the supply and the obligations are committed together.
/// </para>
/// <para>
/// The issue is completed rather than invalidated. Nothing here was unlawful: the offering simply did
/// not fill, and <see cref="PropertyStatus.Invalidated"/> means something else entirely (§73).
/// </para>
/// </remarks>
public sealed class ClosePlacementUnsubscribedCommandHandler
    : IRequestHandler<ClosePlacementUnsubscribedCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IInvestmentRepository _investments;
    private readonly InvestmentUnwinder _unwinder;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public ClosePlacementUnsubscribedCommandHandler(
        IPropertyRepository properties,
        IInvestmentRepository investments,
        InvestmentUnwinder unwinder,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _properties = properties;
        _investments = investments;
        _unwinder = unwinder;
        _currentUser = currentUser;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(ClosePlacementUnsubscribedCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(Error.Unauthorized("property.unauthorized", "Authentication is required."));
        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(Error.Forbidden("property.forbidden", "Only administrators can unwind a placement."));
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure(Error.Validation("placement.reasonRequired", "A reason is required."));

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        // Everything that is still standing: reserved applications hold shares out of the pool, active
        // ones were actually placed. Both have to be undone, and they are undone differently.
        var live = (await _investments.ListAsync(null, property.Id, int.MaxValue, ct))
            .Where(i => i.Status is InvestmentStatus.Reserved or InvestmentStatus.Active)
            .ToList();

        var refunded = 0;
        var owed = 0m;

        foreach (var investment in live)
        {
            var wasActive = investment.Status == InvestmentStatus.Active;

            try
            {
                investment.Annul(request.Reason);
            }
            catch (DomainException ex)
            {
                return Result.Failure(Error.Conflict("placement.cannotUnwind", ex.Message));
            }

            _investments.Update(investment);
            property.ReleaseTokens(investment.TokenCount);

            await _unwinder.UnwindAsync(
                property, investment, wasActive,
                $"Размещение объекта «{property.Name}» не состоялось: {request.Reason}",
                RefundReason.PlacementNotSubscribed, "placement-unsubscribed",
                "PlacementNotSubscribed", recordRefund: true, ct);

            if (wasActive)
            {
                refunded++;
                owed += investment.Amount;
            }
        }

        // Sales stop before the status moves: an offering being unwound must not take one more
        // application in the moment between.
        property.PauseSales();

        if (property.Status == PropertyStatus.Open)
        {
            try
            {
                property.Complete();
            }
            catch (DomainException ex)
            {
                return Result.Failure(Error.Conflict("placement.cannotClose", ex.Message));
            }
        }

        _properties.Update(property);

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PlacementNotSubscribed,
            $"Размещение объекта «{property.Name}» признано несостоявшимся: {request.Reason}. "
            + $"Заявок отменено: {live.Count}, к возврату: {refunded} на {owed:0.##} {property.Currency}",
            AuditSeverity.Alert, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
