using Atria.Domain.Common;
using Atria.Domain.Realtors;
using FluentAssertions;

namespace Atria.Domain.Tests.Realtors;

public sealed class RealtorProfileTests
{
    [Fact]
    public void Create_PopulatesAllFields()
    {
        var userId = Guid.NewGuid();

        var profile = RealtorProfile.Create(
            userId,
            fullName: "Иванов Иван Иванович",
            position: "Старший риелтор",
            walletAddress: "0xabc123",
            companyName: "ООО «Атрия»",
            companyRegistrationNumber: "1027700132195",
            officeAddress: "Бишкек, Чуй 136");

        profile.UserId.Should().Be(userId);
        profile.FullName.Should().Be("Иванов Иван Иванович");
        profile.Position.Should().Be("Старший риелтор");
        profile.WalletAddress.Should().Be("0xabc123");
        profile.CompanyName.Should().Be("ООО «Атрия»");
        profile.CompanyRegistrationNumber.Should().Be("1027700132195");
        profile.OfficeAddress.Should().Be("Бишкек, Чуй 136");
    }

    [Fact]
    public void Create_OnlyFullNameRequired_OptionalsDefaultToNull()
    {
        var profile = RealtorProfile.Create(Guid.NewGuid(), "Иванов Иван");

        profile.Position.Should().BeNull();
        profile.WalletAddress.Should().BeNull();
        profile.CompanyName.Should().BeNull();
        profile.CompanyRegistrationNumber.Should().BeNull();
        profile.OfficeAddress.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenFullNameMissing_Throws(string fullName)
    {
        var act = () => RealtorProfile.Create(Guid.NewGuid(), fullName);

        act.Should().Throw<DomainException>().WithMessage("*full name is required*");
    }

    [Fact]
    public void Create_WhenUserIdEmpty_Throws()
    {
        var act = () => RealtorProfile.Create(Guid.Empty, "Иванов Иван");

        act.Should().Throw<DomainException>().WithMessage("*User is required*");
    }
}
