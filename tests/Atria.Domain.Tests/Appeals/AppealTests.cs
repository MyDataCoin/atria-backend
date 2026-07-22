using Atria.Domain.Appeals;
using Atria.Domain.Common;
using FluentAssertions;

namespace Atria.Domain.Tests.Appeals;

/// <summary>Covers the <see cref="Appeal"/> aggregate: required message, trimming, optional username.</summary>
public sealed class AppealTests
{
    [Fact]
    public void Create_trims_and_stores_username_and_message()
    {
        var appeal = Appeal.Create("  marat  ", "  прошу разобраться  ");

        appeal.Username.Should().Be("marat");
        appeal.Message.Should().Be("прошу разобраться");
        appeal.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_allows_a_blank_username()
    {
        Appeal.Create(null, "текст").Username.Should().BeEmpty();
        Appeal.Create("   ", "текст").Username.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_an_empty_message(string message)
    {
        var act = () => Appeal.Create("marat", message);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_an_over_long_message()
    {
        var act = () => Appeal.Create("marat", new string('x', Appeal.MaxMessageLength + 1));
        act.Should().Throw<DomainException>();
    }
}
