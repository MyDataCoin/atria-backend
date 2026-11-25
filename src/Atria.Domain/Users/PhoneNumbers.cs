namespace Atria.Domain.Users;

/// <summary>
/// Canonical phone numbers: E.164, "+" followed by digits. Kyrgyzstan is the home country, so its
/// local forms ("0700 123 456", "700123456") are understood without a code; any other country needs
/// the international form ("+420 774 958 325", "00420…").
/// </summary>
public static class PhoneNumbers
{
    public const string KyrgyzCountryCode = "996";

    public const string Example = "+996700123456";

    // E.164: at most 15 digits including the country code; 8 is the shortest real numbering plan.
    private const int MinInternationalDigits = 8;
    private const int MaxInternationalDigits = 15;

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return false;

        var international = trimmed.StartsWith('+');
        if (!international && digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
            international = true;
        }

        // Kyrgyz numbers keep their exact rules — a +996 number of the wrong length is a typo,
        // not some other country's number.
        if (!international || digits.StartsWith(KyrgyzCountryCode, StringComparison.Ordinal))
            return TryNormalizeKyrgyz(digits, out normalized);

        if (digits.Length is < MinInternationalDigits or > MaxInternationalDigits || digits[0] == '0')
            return false;

        normalized = "+" + digits;
        return true;
    }

    public static bool IsValid(string? input) => TryNormalize(input, out _);

    public static string Normalize(string input)
        => TryNormalize(input, out var normalized) ? normalized : input.Trim();

    private static bool TryNormalizeKyrgyz(string digits, out string normalized)
    {
        normalized = string.Empty;

        string national;
        if (digits.Length == 12 && digits.StartsWith(KyrgyzCountryCode, StringComparison.Ordinal))
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

        normalized = "+" + KyrgyzCountryCode + national;
        return true;
    }
}
