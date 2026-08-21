namespace Atria.Domain.Investments;

/// <summary>
/// The identity of an issue as the token contract carries it: <see cref="Property.Id"/> written into
/// the contract's immutable <c>propertyId</c> at deployment.
/// </summary>
/// <remarks>
/// <para>
/// This is what ties a contract to the issue it represents. Without it a token is an anonymous ERC-20
/// that the platform merely asserts belongs to a property, and a wrong address recorded once is
/// indistinguishable from a right one — the register would keep reconciling happily against somebody
/// else's shares.
/// </para>
/// <para>
/// The encoding is the UUID's canonical 16 bytes, left-aligned in the 32-byte word and zero-padded
/// on the right — the same way a fixed-size byte string is laid out in a <c>bytes32</c>. Left, not
/// right: the id is a byte string, not a number, and reading it back out of a block explorer should
/// be a matter of taking the first 32 hex digits.
/// </para>
/// </remarks>
public static class PropertyChainId
{
    /// <summary>Length of a <c>bytes32</c> in hex digits.</summary>
    private const int Bytes32HexLength = 64;

    /// <summary>Length of a UUID in hex digits.</summary>
    private const int GuidHexLength = 32;

    /// <summary>The all-zero word: a contract deployed without its issue's id filled in.</summary>
    public const string Unset = "0x" + "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>The <c>bytes32</c> value to deploy an issue's token contract with, lowercase hex.</summary>
    public static string From(Guid propertyId)
        => "0x" + propertyId.ToString("N") + new string('0', Bytes32HexLength - GuidHexLength);

    /// <summary>
    /// Reads an issue id back out of a <c>bytes32</c> as read from a contract. False when the word is
    /// not a well-formed id — the zero placeholder included, which identifies nothing.
    /// </summary>
    public static bool TryParse(string? value, out Guid propertyId)
    {
        propertyId = Guid.Empty;

        var hex = value?.Trim() ?? string.Empty;
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        if (hex.Length != Bytes32HexLength)
            return false;

        // The padding is part of the encoding: a word whose tail is not zero was not written by us,
        // and taking its first half anyway would invent an id out of an unrelated value.
        if (hex.AsSpan(GuidHexLength).ContainsAnyExcept('0'))
            return false;

        return Guid.TryParseExact(hex[..GuidHexLength], "N", out propertyId)
            && propertyId != Guid.Empty;
    }

    /// <summary>True when a word read from a contract is the id of this issue.</summary>
    public static bool Matches(Guid propertyId, string? onChainValue)
        => TryParse(onChainValue, out var parsed) && parsed == propertyId;
}
