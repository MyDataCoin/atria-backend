using Atria.Application.Deals;
using Atria.Application.Deals.Commands;
using Atria.Application.Deals.Validators;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Atria.Application.Tests.Deals;

/// <summary>
/// Regression cover for the commission finding of the 2026-08-18 review: the create-deal request
/// carried the realtor's own commission percent, bounded only by the domain's 0–100, and the deal
/// settles by itself on the first purchase through the referral link. A realtor could therefore
/// book 100% of every investment made through their links as commission, with no approval step
/// anywhere in the flow. The ceiling is configuration (DealCommission:MaxPercent, default 10).
/// </summary>
public sealed class CreateDealCommissionCapTests
{
    private static CreateDealCommandValidator Validator(decimal maxPercent = 10m) =>
        new(Options.Create(new DealCommissionOptions { MaxPercent = maxPercent }));

    [Theory]
    [InlineData(100)]
    [InlineData(50)]
    [InlineData(10.01)]
    public void Commission_above_the_configured_ceiling_is_rejected(decimal percent)
    {
        var result = Validator().Validate(new CreateDealCommand(Guid.NewGuid(), percent));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateDealCommand.CommissionPercent));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public void Commission_within_the_ceiling_is_accepted(decimal percent)
    {
        Validator().Validate(new CreateDealCommand(Guid.NewGuid(), percent)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void The_ceiling_comes_from_configuration_not_from_code()
    {
        // Raising the limit must be a settings change, not a rebuild.
        Validator(maxPercent: 25m)
            .Validate(new CreateDealCommand(Guid.NewGuid(), 20m))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Default_ceiling_is_ten_percent()
    {
        new DealCommissionOptions().MaxPercent.Should().Be(10m);
    }
}
