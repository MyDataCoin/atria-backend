using Atria.Domain.Users;
using FluentAssertions;

namespace Atria.Domain.Tests.Users;

/// <summary>
/// Covers Kyrgyzstan phone normalization/validation — the primary auth identifier.
/// All accepted input forms must canonicalize to +996XXXXXXXXX.
/// </summary>
public sealed class PhoneNumbersTests
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
        PhoneNumbers.TryNormalize(input, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
        PhoneNumbers.IsValid(input).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]              // too short
    [InlineData("0100123456")]         // national first digit '1' is invalid
    [InlineData("+99670012")]          // +996 of the wrong length is a typo, not another country
    [InlineData("+0123456789")]        // no country code starts with 0
    [InlineData("+1234567890123456")]  // longer than E.164 allows
    [InlineData("9967001234567")]      // too long
    [InlineData("abcdefghi")]          // not digits
    public void IsValid_rejects_non_kg_numbers(string? input)
    {
        PhoneNumbers.IsValid(input).Should().BeFalse();
        PhoneNumbers.TryNormalize(input, out var normalized).Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [Theory]
    [InlineData("+420 774 958 325", "+420774958325")]
    [InlineData("+7 (701) 123-45-67", "+77011234567")]
    [InlineData("00420774958325", "+420774958325")]
    [InlineData("+1 555 123 4567", "+15551234567")]
    public void International_numbers_are_accepted_in_E164(string input, string expected)
    {
        PhoneNumbers.TryNormalize(input, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Fact]
    public void Normalize_returns_trimmed_input_when_not_normalizable()
        => PhoneNumbers.Normalize("  not-a-phone  ").Should().Be("not-a-phone");
}
