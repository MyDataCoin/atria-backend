using System.Text.RegularExpressions;
using Atria.Domain.Common;

namespace Atria.Domain.Compliance;

/// <summary>
/// EVM/BEP-20 wallet address value object. Validates the canonical
/// <c>0x</c> + 40 hex-digit form. Equality is by normalized value.
/// </summary>
public sealed partial class WalletAddress : ValueObject
{
    // ^0x[a-fA-F0-9]{40}$ — 20-byte hex address with the 0x prefix.
    [GeneratedRegex("^0x[a-fA-F0-9]{40}$", RegexOptions.Compiled)]
    private static partial Regex AddressRegex();

    public string Value { get; }

    private WalletAddress(string value) => Value = value;

    /// <summary>Creates a validated address; throws <see cref="DomainException"/> if malformed.</summary>
    public static WalletAddress Create(string value)
    {
        if (!IsValid(value))
            throw new DomainException("Invalid EVM wallet address.");

        return new WalletAddress(value);
    }

    /// <summary>Non-throwing variant; returns false and a null address when invalid.</summary>
    public static bool TryCreate(string value, out WalletAddress? addr)
    {
        if (IsValid(value))
        {
            addr = new WalletAddress(value);
            return true;
        }

        addr = null;
        return false;
    }

    /// <summary>True if the string matches the EVM address format.</summary>
    public static bool IsValid(string value)
        => !string.IsNullOrWhiteSpace(value) && AddressRegex().IsMatch(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
