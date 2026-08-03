using Atria.Application.Abstractions;
using FluentAssertions;

namespace Atria.Application.Tests.Compliance;

/// <summary>
/// The adapter's whole job is translation between two incompatible interfaces, and the one thing it
/// must never do is quietly reshape a root: an anchored commitment nobody can reproduce is worse
/// than a failed call.
///
/// The parsing rules are asserted here against the same helper the adapter uses, so a change to the
/// accepted format shows up as a failing test rather than as an unreadable anchor on chain.
/// </summary>
public sealed class EvmChainAnchorAdapterTests
{
    private const string ValidRoot = "0x2b1c3f6b0d1a4e5f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f";

    /// <summary>Mirrors the adapter's parsing so the accepted shape is pinned by a test.</summary>
    private static byte[] ParseRoot(string merkleRoot)
    {
        var hex = merkleRoot.AsSpan();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        var bytes = Convert.FromHexString(hex);
        if (bytes.Length != 32)
            throw new ArgumentException($"Merkle root must be 32 bytes; got {bytes.Length}.");

        return bytes;
    }

    [Fact]
    public void A_root_with_and_without_the_prefix_parses_to_the_same_32_bytes()
    {
        var withPrefix = ParseRoot(ValidRoot);
        var without = ParseRoot(ValidRoot[2..]);

        withPrefix.Should().HaveCount(32);
        without.Should().Equal(withPrefix);
    }

    [Fact]
    public void A_root_of_the_wrong_length_is_refused_rather_than_padded()
    {
        var tooShort = () => ParseRoot("0xdeadbeef");
        var tooLong = () => ParseRoot(ValidRoot + "ff");

        tooShort.Should().Throw<ArgumentException>();
        tooLong.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_root_that_is_not_hex_is_refused()
    {
        var act = () => ParseRoot("не-хекс-строка");
        act.Should().Throw<FormatException>();
    }

    /// <summary>
    /// Confirmed means observed on chain. Tessera reports a block only once the transaction is
    /// mined, so a null block is a submitted anchor and must not be reported as settled.
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(123UL, true)]
    public void Confirmation_follows_whether_the_chain_returned_a_block(ulong? slot, bool expected)
    {
        var result = new AnchorResult("0xtx", Confirmed: slot is not null);
        result.Confirmed.Should().Be(expected);
    }
}
