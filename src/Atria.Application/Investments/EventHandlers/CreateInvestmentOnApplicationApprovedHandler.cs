using Atria.Application.Abstractions;
using Atria.Domain.Applications.Events;
using Atria.Domain.Factories;

namespace Atria.Application.Investments.EventHandlers;

/// <summary>
/// Creates the (PendingPayment) <see cref="Atria.Domain.Investments.Investment"/> when an application is
/// approved. Idempotent: guarded by both the processed-event ledger and a check for an
/// existing investment, so an at-least-once redelivery never creates a duplicate.
/// </summary>
public sealed class CreateInvestmentOnApplicationApprovedHandler
    : IDomainEventHandler<ApplicationApprovedEvent>
{
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;
    private readonly IProcessedEventStore _processedEvents;
    private readonly IUnitOfWork _unitOfWork;

    public CreateInvestmentOnApplicationApprovedHandler(
        IInvestmentRepository investments,
        IPropertyRepository properties,
        IProcessedEventStore processedEvents,
        IUnitOfWork unitOfWork)
    {
        _investments = investments;
        _properties = properties;
        _processedEvents = processedEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(ApplicationApprovedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processedEvents.IsProcessedAsync(key, ct))
            return;

        // Second guard: an investment may already exist for this application.
        var existing = await _investments.GetByApplicationIdAsync(domainEvent.ApplicationId, ct);
        if (existing is not null)
        {
            await _processedEvents.MarkProcessedAsync(key, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return;
        }

        // The property defines the settlement currency for the investment.
        var property = await _properties.GetByIdAsync(domainEvent.PropertyId, ct);
        if (property is null)
            throw new InvalidOperationException(
                $"Property {domainEvent.PropertyId} not found while creating investment for application {domainEvent.ApplicationId}.");

        var investment = InvestmentFactory.CreateFromApprovedApplication(
            domainEvent.ApplicationId, domainEvent.InvestorId, domainEvent.PropertyId,
            domainEvent.Amount, property.Currency);

        await _investments.AddAsync(investment, ct);
        await _processedEvents.MarkProcessedAsync(key, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
