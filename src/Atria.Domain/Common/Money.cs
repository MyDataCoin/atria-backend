namespace Atria.Domain.Common;

/// <summary>
/// The currency the platform issues, prices, and pays in: the Kyrgyzstani som.
/// <para>
/// Every issue is a Kyrgyzstan property offered to Kyrgyzstan investors, so there is exactly one
/// currency and it is not a choice an operator makes per object. Leaving it a free-form field is
/// what let a demo object be priced in Tajikistani somoni — a three-letter code passes a
/// "three letters" check just as well as the right one, and nothing downstream converts, so a
/// wrong code silently relabels every amount on the object rather than failing.
/// </para>
/// <para>
/// Not applied to <see cref="Atria.Domain.TravelRule.TravelRuleMessage"/>: a transfer notice can
/// legitimately name a counterparty VASP's currency, which the platform reports rather than sets.
/// </para>
/// </summary>
public static class Money
{
    /// <summary>ISO 4217 code of the only currency the platform issues in.</summary>
    public const string Currency = "KGS";

    /// <summary>How the som is written for a person, in Russian.</summary>
    public const string Symbol = "сом";

    /// <summary>
    /// Returns <paramref name="currency"/> as the canonical <see cref="Currency"/>, accepting any
    /// capitalisation. Throws when it names anything else — the caller is trying to denominate an
    /// issue in a currency the platform does not operate in.
    /// </summary>
    public static string Require(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");

        if (!string.Equals(currency.Trim(), Currency, StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                $"The platform issues in {Currency} ({Symbol}) only; '{currency}' is not supported.");

        return Currency;
    }

    /// <summary>Whether <paramref name="currency"/> names the som. For validators, which report rather than throw.</summary>
    public static bool IsSom(string? currency)
        => !string.IsNullOrWhiteSpace(currency)
           && string.Equals(currency.Trim(), Currency, StringComparison.OrdinalIgnoreCase);
}
