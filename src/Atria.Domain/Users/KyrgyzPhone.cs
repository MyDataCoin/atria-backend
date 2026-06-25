namespace Atria.Domain.Users;

/// <summary>
/// Normalization + validation for Kyrgyzstan (KG) mobile numbers, the platform's primary
/// auth/registration identifier. Canonical form is <c>+996XXXXXXXXX</c> (country code 996
/// followed by 9 national digits, the first in 2–9).
/// <para>Accepts and canonicalizes the common ways users type a KG number:</para>
/// <list type="bullet">
///   <item><description><c>+996700123456</c> → <c>+996700123456</c></description></item>
///   <item><description><c>996700123456</c> → <c>+996700123456</c></description></item>
///   <item><description><c>0700123456</c> (national trunk) → <c>+996700123456</c></description></item>
///   <item><description><c>700123456</c> (9 digits) → <c>+996700123456</c></description></item>
///   <item><description>spaces / dashes / parentheses are ignored</description></item>
/// </list>
/// </summary>
public static class KyrgyzPhone
{
    public const string CountryCode = "996";

    /// <summary>Example of the canonical format, for messages/docs.</summary>
    public const string Example = "+996700123456";

    /// <summary>
    /// Tries to canonicalize <paramref name="input"/> to <c>+996XXXXXXXXX</c>.
    /// Returns false if it is not a recognizable KG mobile number.
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Keep digits only (drops '+', spaces, dashes, parentheses).
        var digits = new string(input.Where(char.IsDigit).ToArray());

        string national;
        if (digits.Length == 12 && digits.StartsWith(CountryCode, StringComparison.Ordinal))
            national = digits[3..];                 // 996 + 9 national
        else if (digits.Length == 10 && digits[0] == '0')
            national = digits[1..];                 // 0 + 9 national (trunk prefix)
        else if (digits.Length == 9)
            national = digits;                      // bare 9 national digits
        else
            return false;

        // First national digit must be 2–9 (KG mobile/landline ranges; excludes 0/1).
        if (national[0] is < '2' or > '9')
            return false;

        normalized = "+" + CountryCode + national;
        return true;
    }

    /// <summary>True if <paramref name="input"/> is a valid KG number in any accepted form.</summary>
    public static bool IsValid(string? input) => TryNormalize(input, out _);

    /// <summary>Canonicalizes to <c>+996XXXXXXXXX</c>, or returns the trimmed input unchanged if it cannot.</summary>
    public static string Normalize(string input)
        => TryNormalize(input, out var normalized) ? normalized : input.Trim();
}
