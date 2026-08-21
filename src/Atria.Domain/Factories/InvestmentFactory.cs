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
    /// <remarks>
    /// The amount is derived from the count and the price rather than passed in: the investor pays
    /// for the whole tokens they get and not for the sum they happened to type, so there is no
    /// remainder to explain and no way for the two numbers to disagree.
    /// </remarks>
    public static Investment CreateForInvestor(
        Guid investorId, Guid propertyId, long tokenCount, string currency,
        decimal pricePerToken, DateTime reservedUntilUtc, string? referralToken = null)
    {
        if (tokenCount <= 0)
            throw new DomainException("Token count must be positive.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");
        if (pricePerToken <= 0)
            throw new DomainException("Price per token must be positive.");

        var amount = TokenAmount.CostOf(tokenCount, pricePerToken);

        var investment = Investment.CreateReserved(
            investorId, propertyId, tokenCount, amount, currency, pricePerToken, reservedUntilUtc, referralToken);

        investment.RaiseDomainEvent(new InvestmentCreatedEvent(
            investment.Id, investorId, propertyId, tokenCount, amount));

        return investment;
    }
}
