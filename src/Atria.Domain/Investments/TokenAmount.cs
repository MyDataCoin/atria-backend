using System.Numerics;
using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// The granularity of a share. A holding is a decimal, so an issue can be sized to something real —
/// 57.55 tokens for a 57.55 m² apartment — and an investor can buy a part of one. A share divides
/// into hundredths and no further.
/// <para>
/// Everything that counts tokens rounds to <see cref="Scale"/> decimals and nothing anywhere may
/// create a finer amount. That is what lets the register, the payout split and the token contract
/// agree: below this scale there is no such thing as a share, so there is nothing left to disagree
/// about. On chain the same number is held as an integer of minor units (<see cref="ToMinor"/>),
/// which is why the contract's <c>decimals()</c> must equal <see cref="Scale"/>.
/// </para>
/// </summary>
public static class TokenAmount
{
    /// <summary>Decimal places a share is divisible into. The smallest holding is 0.01.</summary>
    public const int Scale = 2;

    /// <summary>The smallest non-zero holding.</summary>
    public static readonly decimal Smallest = 1m / Pow10(Scale);

    /// <summary>
    /// Rounds to the share granularity, half away from zero. Use for an amount that is already
    /// meant to be this size and only needs its representation pinned (a snapshot, a chain read).
    /// </summary>
    public static decimal Normalize(decimal tokens)
        => decimal.Round(tokens, Scale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rounds DOWN to the share granularity. Use when deriving a holding from money: the investor
    /// gets what their money covers and never a sliver more, so the issue cannot be oversold by
    /// rounding.
    /// </summary>
    public static decimal Floor(decimal tokens)
    {
        var truncated = decimal.Truncate(tokens * Pow10(Scale)) / Pow10(Scale);
        return truncated;
    }

    /// <summary>
    /// The amount as a whole number of minor units — the integer the token contract holds and the
    /// only form in which token maths stays exact. Throws when the value is finer than
    /// <see cref="Scale"/>, because silently rounding here would put the register and the chain on
    /// different numbers.
    /// </summary>
    public static BigInteger ToMinor(decimal tokens)
    {
        var scaled = tokens * Pow10(Scale);
        if (scaled != decimal.Truncate(scaled))
            throw new DomainException(
                $"Token amount {tokens} is finer than the share granularity ({Scale} decimals).");

        return (BigInteger)scaled;
    }

    /// <summary>Turns minor units (as held on chain) back into a share amount.</summary>
    public static decimal FromMinor(BigInteger minor) => (decimal)minor / Pow10(Scale);

    /// <summary>True when the amount is already expressible at the share granularity.</summary>
    public static bool IsWellFormed(decimal tokens)
    {
        var scaled = tokens * Pow10(Scale);
        return scaled == decimal.Truncate(scaled);
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
            result *= 10m;
        return result;
    }
}
