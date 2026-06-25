using Atria.Domain.Common;
using Atria.Domain.Compliance;
using FluentAssertions;

namespace Atria.Domain.Tests.Compliance;

public sealed class WalletAddressTests
{
    private const string ValidLower = "0x" + "abcdef0123456789abcdef0123456789abcdef01";
    private const string ValidUpper = "0x" + "ABCDEF0123456789ABCDEF0123456789ABCDEF01";

    [Theory]
    [InlineData(ValidLower)]
    [InlineData(ValidUpper)]
    public void Create_WithValidAddress_Succeeds(string value)
    {
        // Act
        var address = WalletAddress.Create(value);

        // Assert
        address.Value.Should().Be(value);
        address.ToString().Should().Be(value);
    }

    [Theory]
    [InlineData(ValidLower, true)]
    [InlineData(ValidUpper, true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("0x123", false)]                                                  // too short
    [InlineData("abcdef0123456789abcdef0123456789abcdef01", false)]              // missing 0x prefix
    [InlineData("0xZZcdef0123456789abcdef0123456789abcdef01", false)]            // non-hex chars
    [InlineData("0xabcdef0123456789abcdef0123456789abcdef012", false)]           // 41 hex (too long)
    public void IsValid_ReturnsExpected(string value, bool expected)
    {
        // Act / Assert
        WalletAddress.IsValid(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("0x123")]
    [InlineData("0xGGGGdef0123456789abcdef0123456789abcdef01")]
    public void Create_WithInvalidAddress_ThrowsDomainException(string value)
    {
        // Act
        var act = () => WalletAddress.Create(value);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*Invalid EVM wallet address*");
    }

    [Fact]
    public void TryCreate_WithValidAddress_ReturnsTrueAndAddress()
    {
        // Act
        var ok = WalletAddress.TryCreate(ValidLower, out var address);

        // Assert
        ok.Should().BeTrue();
        address.Should().NotBeNull();
        address!.Value.Should().Be(ValidLower);
    }

    [Fact]
    public void TryCreate_WithInvalidAddress_ReturnsFalseAndNull()
    {
        // Act
        var ok = WalletAddress.TryCreate("nope", out var address);

        // Assert
        ok.Should().BeFalse();
        address.Should().BeNull();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        // Arrange
        var a = WalletAddress.Create(ValidLower);
        var b = WalletAddress.Create(ValidLower);

        // Assert
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DiffersByCase_BecauseValueIsNotNormalized()
    {
        // Arrange
        var lower = WalletAddress.Create(ValidLower);
        var upper = WalletAddress.Create(ValidUpper);

        // Assert
        // Both are valid but the value object stores the raw string, so equality is case-sensitive.
        lower.Should().NotBe(upper);
    }
}
