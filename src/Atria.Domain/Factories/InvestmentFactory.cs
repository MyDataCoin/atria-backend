using Atria.Domain.Common;
using Atria.Domain.Investments;
using Atria.Domain.Investments.Events;

namespace Atria.Domain.Factories;

/// <summary>
/// Factory Method: builds a valid initial <see cref="Investment"/> (Reserved) directly for an
/// investor and records the creation event. Reserving the property's tokens is the caller's
/// responsibility (done in the same unit of work) so the supply cannot be oversubscribed.
/// </summary>
public static class InvestmentFactory
{
    public static Investment CreateForInvestor(
        Guid investorId, Guid propertyId, long tokenCount, decimal amount, string currency,
        decimal pricePerToken, DateTime reservedUntilUtc, string? referralToken = null)
    {
        if (tokenCount <= 0)
            throw new DomainException("Token count must be positive.");
        if (amount <= 0)
            throw new DomainException("Investment amount must be positive.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");
        if (pricePerToken <= 0)
            throw new DomainException("Price per token must be positive.");

        var investment = Investment.CreateReserved(
            investorId, propertyId, tokenCount, amount, currency, pricePerToken, reservedUntilUtc, referralToken);

        investment.RaiseDomainEvent(new InvestmentCreatedEvent(
            investment.Id, investorId, propertyId, amount));

        return investment;
    }
}
