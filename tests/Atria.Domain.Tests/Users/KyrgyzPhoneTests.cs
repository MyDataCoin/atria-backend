using Atria.Domain.Users;
using FluentAssertions;

namespace Atria.Domain.Tests.Users;

/// <summary>
/// Covers Kyrgyzstan phone normalization/validation — the primary auth identifier.
/// All accepted input forms must canonicalize to +996XXXXXXXXX.
/// </summary>
public sealed class KyrgyzPhoneTests
{
    [Theory]
    [InlineData("+996700123456", "+996700123456")]   // already canonical
    [InlineData("996700123456", "+996700123456")]    // missing '+'
    [InlineData("0700123456", "+996700123456")]      // national trunk prefix
    [InlineData("700123456", "+996700123456")]       // bare 9 national digits
    [InlineData("+996 700 123-456", "+996700123456")] // spaces/dashes
    [InlineData(" (996) 555-987654 ", "+996555987654")] // parens + spaces
    public void TryNormalize_canonicalizes_accepted_forms(string input, string expected)
    {
        KyrgyzPhone.TryNormalize(input, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
        KyrgyzPhone.IsValid(input).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]              // too short
    [InlineData("0100123456")]         // national first digit '1' is invalid
    [InlineData("+1 555 123 4567")]    // not a KG number
    [InlineData("9967001234567")]      // too long
    [InlineData("abcdefghi")]          // not digits
    public void IsValid_rejects_non_kg_numbers(string? input)
    {
        KyrgyzPhone.IsValid(input).Should().BeFalse();
        KyrgyzPhone.TryNormalize(input, out var normalized).Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_returns_trimmed_input_when_not_normalizable()
        => KyrgyzPhone.Normalize("  not-a-phone  ").Should().Be("not-a-phone");
}
