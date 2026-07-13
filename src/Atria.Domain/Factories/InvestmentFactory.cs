using Atria.Domain.Common;
using Atria.Domain.Investments;
using Atria.Domain.Investments.Events;

namespace Atria.Domain.Factories;

/// <summary>
/// Factory Method: builds a valid initial <see cref="Investment"/> (PendingPayment) directly
/// for an investor and records the creation event.
/// </summary>
public static class InvestmentFactory
{
    public static Investment CreateForInvestor(
        Guid investorId, Guid propertyId, long tokenCount, decimal amount, string currency,
        string? referralToken = null)
    {
        if (tokenCount <= 0)
            throw new DomainException("Token count must be positive.");
        if (amount <= 0)
            throw new DomainException("Investment amount must be positive.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");

        var investment = Investment.CreatePending(
            investorId, propertyId, tokenCount, amount, currency, referralToken);

        investment.RaiseDomainEvent(new InvestmentCreatedEvent(
            investment.Id, investorId, propertyId, amount));

        return investment;
    }
}
