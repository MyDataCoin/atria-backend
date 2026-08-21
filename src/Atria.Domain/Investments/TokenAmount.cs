using System.Numerics;
using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// The granularity of a share: there isn't any. A token is one indivisible share of an issue, so
/// every count in the platform is a whole number and the smallest holding is 1.
/// <para>
/// The token is deliberately NOT a square metre. Sizing the issue by area forces the unit price up
/// to the price of a metre, and any purchase under that price then has to be expressed as a
/// fraction — which the contract, whose <c>decimals()</c> is <see cref="Scale"/>, cannot mint.
/// Instead an issue is cut into enough shares that the unit price is small against the minimum
/// entry, and area and ownership percentage are shown as derived equivalents.
/// </para>
/// </summary>
public static class TokenAmount
{
    /// <summary>The token contract's <c>decimals()</c>. Zero: a share does not divide.</summary>
    public const int Scale = 0;

    /// <summary>The smallest non-zero holding — one whole token.</summary>
    public const long Smallest = 1;

    /// <summary>
    /// How many whole tokens <paramref name="amount"/> buys at <paramref name="pricePerToken"/>,
    /// rounded DOWN. The investor gets what their money covers and never a sliver more, so the issue
    /// cannot be oversold by rounding. Callers charge for the returned count, not for the amount
    /// asked for — see <see cref="CostOf"/>.
    /// </summary>
    public static long FromMoney(decimal amount, decimal pricePerToken)
    {
        if (pricePerToken <= 0)
            throw new DomainException("Token price must be positive.");
        if (amount <= 0)
            return 0;

        return (long)decimal.Floor(amount / pricePerToken);
    }

    /// <summary>What <paramref name="tokens"/> cost at <paramref name="pricePerToken"/>.</summary>
    public static decimal CostOf(long tokens, decimal pricePerToken) => tokens * pricePerToken;

    /// <summary>
    /// The count as the integer the token contract holds. An identity while <see cref="Scale"/> is
    /// zero, kept as the single place the chain's unit is derived so a future <c>decimals()</c>
    /// change has one seam to move.
    /// </summary>
    public static BigInteger ToMinor(long tokens)
    {
        if (tokens < 0)
            throw new DomainException($"Token amount {tokens} cannot be negative.");

        return tokens;
    }

    /// <summary>Turns minor units (as held on chain) back into a share count.</summary>
    public static long FromMinor(BigInteger minor)
    {
        if (minor < 0 || minor > long.MaxValue)
            throw new DomainException($"On-chain balance {minor} is outside the representable share range.");

        return (long)minor;
    }
}
