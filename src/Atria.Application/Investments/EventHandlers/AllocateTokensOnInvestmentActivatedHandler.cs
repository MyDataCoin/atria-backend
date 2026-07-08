using Atria.Application.Abstractions;
using Atria.Domain.Investments.Events;

namespace Atria.Application.Investments.EventHandlers;

/// <summary>
/// Decrements the property's available token supply when an investment becomes Active.
/// This is the authoritative point where tokens leave supply (preventing oversubscription).
/// Idempotent via the processed-event ledger, so an at-least-once redelivery never
/// double-allocates. Auto-discovered by the DI assembly scan.
/// </summary>
public sealed class AllocateTokensOnInvestmentActivatedHandler
    : IDomainEventHandler<InvestmentActivatedEvent>
{
    private readonly IPropertyRepository _properties;
    private readonly IProcessedEventStore _processedEvents;
    private readonly IUnitOfWork _unitOfWork;

    public AllocateTokensOnInvestmentActivatedHandler(
        IPropertyRepository properties,
        IProcessedEventStore processedEvents,
        IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _processedEvents = processedEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(InvestmentActivatedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processedEvents.IsProcessedAsync(key, ct))
            return;

        var property = await _properties.GetByIdAsync(domainEvent.PropertyId, ct);
        if (property is null)
            throw new InvalidOperationException(
                $"Property {domainEvent.PropertyId} not found while allocating tokens for investment {domainEvent.InvestmentId}.");

        // The token count was fixed when the investment was created; allocate exactly that.
        if (domainEvent.TokenCount > 0)
            property.AllocateTokens(domainEvent.TokenCount); // throws if it would oversubscribe the supply

        _properties.Update(property);
        await _processedEvents.MarkProcessedAsync(key, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
