using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Compliance;
using Atria.Domain.Refunds;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Declares an issue invalid (draft Decree, §73). Admin only.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="Reason">The ground on which it is declared invalid. Required and journalled.</param>
public sealed record InvalidateIssueCommand(Guid PropertyId, string Reason) : IRequest<Result>;

/// <summary>
/// Carries out everything §73 entails, in one unit of work: the issue is marked invalid and its sales
/// stop, every holder's shares are queued for withdrawal from circulation, and the money owed back to
/// each of them is recorded.
///
/// The three go together on purpose. Withdrawing shares without recording the refund would leave
/// holders with neither their shares nor a claim; recording refunds without withdrawing the shares
/// would leave invalid shares trading. Paying the refunds is a separate step — how funds reach an
/// investor is decided outside the platform — but what is owed exists on the books from this moment.
/// </summary>
public sealed class InvalidateIssueCommandHandler : IRequestHandler<InvalidateIssueCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IHolderPositionRepository _positions;
    private readonly IRefundObligationRepository _refunds;
    private readonly IBlockchainOperationQueue _chain;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public InvalidateIssueCommandHandler(
        IPropertyRepository properties,
        IHolderPositionRepository positions,
        IRefundObligationRepository refunds,
        IBlockchainOperationQueue chain,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _properties = properties;
        _positions = positions;
        _refunds = refunds;
        _chain = chain;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(InvalidateIssueCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure(Error.Validation("property.reasonRequired", "A reason is required."));

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        try
        {
            property.Invalidate();
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("property.cannotInvalidate", ex.Message));
        }

        _properties.Update(property);

        var holders = (await _positions.GetByPropertyAsync(property.Id, ct))
            .Where(p => p.TokenCount > 0)
            .ToList();

        foreach (var holder in holders)
        {
            // Refunded at the price the shares were issued at: an invalid issue is unwound, not
            // revalued, so a holder is neither punished nor enriched by what happened since.
            var amount = holder.TokenCount * property.TokenPrice;

            if (holder.InvestorId is not null)
            {
                await _refunds.AddAsync(RefundObligation.Raise(
                    property.Id, holder.InvestorId.Value, holder.WalletAddress, holder.TokenCount,
                    amount, property.Currency, RefundReason.IssueInvalidated, request.Reason), ct);
            }

            if (string.IsNullOrWhiteSpace(property.TokenContractAddress))
                continue;

            var payload = JsonSerializer.Serialize(new
            {
                propertyId = property.Id,
                tokenContractAddress = property.TokenContractAddress,
                chain = property.TokenChain,
                holder = holder.WalletAddress,
                amount = holder.TokenCount,
                reason = "IssueInvalidated"
            });

            await _chain.EnqueueAsync(
                BlockchainOperationType.TokenBurn, payload,
                $"token-burn:invalidated:{property.Id}:{holder.WalletAddress}", ct);
        }

        // An address holding shares with no investor behind it cannot be refunded, and that is a
        // problem someone has to resolve by hand rather than a detail to bury in a debug log.
        var unlinked = holders.Count(h => h.InvestorId is null);

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.IssueInvalidated,
            $"Эмиссия объекта «{property.Name}» признана недействительной: {request.Reason}. "
            + $"Держателей: {holders.Count}, к возврату: {holders.Sum(h => h.TokenCount * property.TokenPrice)} "
            + $"{property.Currency}"
            + (unlinked > 0 ? $". Адресов без связи с инвестором: {unlinked} — возврат вручную" : string.Empty),
            AuditSeverity.Alert, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
