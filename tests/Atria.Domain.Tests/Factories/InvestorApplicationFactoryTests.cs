using Atria.Domain.Applications;
using Atria.Domain.Common;
using Atria.Domain.Factories;
using Atria.Domain.Kyc;
using FluentAssertions;

namespace Atria.Domain.Tests.Factories;

public sealed class InvestorApplicationFactoryTests
{
    [Theory]
    [InlineData(KycStatus.Pending)]
    [InlineData(KycStatus.UnderReview)]
    [InlineData(KycStatus.Rejected)]
    public void Create_WhenKycNotApproved_ThrowsDomainException(KycStatus status)
    {
        // Act
        var act = () => InvestorApplicationFactory.Create(
            Guid.NewGuid(), Guid.NewGuid(), 1000m, status);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*KYC must be approved*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000.50)]
    public void Create_WhenAmountNotPositive_ThrowsDomainException(decimal amount)
    {
        // Act
        var act = () => InvestorApplicationFactory.Create(
            Guid.NewGuid(), Guid.NewGuid(), amount, KycStatus.Approved);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*amount must be positive*");
    }

    [Fact]
    public void Create_WhenKycApprovedAndAmountPositive_ReturnsDraftApplication()
    {
        // Arrange
        var investorId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();

        // Act
        var application = InvestorApplicationFactory.Create(
            investorId, propertyId, 2500m, KycStatus.Approved);

        // Assert
        application.Should().NotBeNull();
        application.Status.Should().Be(ApplicationStatus.Draft);
        application.InvestorId.Should().Be(investorId);
        application.PropertyId.Should().Be(propertyId);
        application.Amount.Should().Be(2500m);
        application.Id.Should().NotBe(Guid.Empty);
        application.DomainEvents.Should().BeEmpty();
    }
}
