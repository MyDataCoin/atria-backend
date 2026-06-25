using Atria.Domain.Common;
using Atria.Domain.Investments;
using Atria.Domain.Investments.Events;

namespace Atria.Domain.Factories;

/// <summary>
/// Factory Method: builds a valid initial <see cref="Investment"/> (PendingPayment) from
/// an approved application and records the creation event.
/// </summary>
public static class InvestmentFactory
{
    public static Investment CreateFromApprovedApplication(
        Guid applicationId, Guid investorId, Guid propertyId, decimal amount, string currency)
    {
        if (amount <= 0)
            throw new DomainException("Investment amount must be positive.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");

        var investment = Investment.CreatePending(applicationId, investorId, propertyId, amount, currency);

        investment.RaiseDomainEvent(new InvestmentCreatedEvent(
            investment.Id, investorId, propertyId, amount, applicationId));

        return investment;
    }
}
