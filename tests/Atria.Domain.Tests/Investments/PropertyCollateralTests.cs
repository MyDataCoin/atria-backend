using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// The collateral file behind an issue: what it is worth, who said so, and the encumbrance that makes
/// the pledge real rather than declared.
/// </summary>
public sealed class PropertyCollateralTests
{
    private static readonly DateTime ValuedAt = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Property NewProperty() =>
        Property.Create("Tower One", null, null, 1_000_000m, 100m, 10_000, "KGS");

    [Fact]
    public void A_new_issue_has_no_collateral_recorded()
    {
        var property = NewProperty();

        property.CollateralValue.Should().BeNull();
        property.CollateralValuedAtUtc.Should().BeNull();
        property.CollateralAppraiser.Should().BeNull();
        property.EncumbranceRegistrationNumber.Should().BeNull();
        property.IssueRegistrationNumber.Should().BeNull();
        property.CollateralManagerUserId.Should().BeNull();
    }

    [Fact]
    public void An_appraisal_records_value_date_and_appraiser_together()
    {
        var property = NewProperty();

        property.SetCollateralAppraisal(1_250_000m, ValuedAt, "ОсОО «Оценка»");

        property.CollateralValue.Should().Be(1_250_000m);
        property.CollateralValuedAtUtc.Should().Be(ValuedAt);
        property.CollateralAppraiser.Should().Be("ОсОО «Оценка»");
    }

    [Fact]
    public void An_appraisal_without_an_appraiser_is_refused()
    {
        var property = NewProperty();

        var set = () => property.SetCollateralAppraisal(1_250_000m, ValuedAt, "  ");

        set.Should().Throw<DomainException>().WithMessage("*Appraiser*");
        property.CollateralValue.Should().BeNull("nothing may be recorded from an incomplete appraisal");
    }

    [Fact]
    public void A_non_positive_appraised_value_is_refused()
    {
        var property = NewProperty();

        var set = () => property.SetCollateralAppraisal(0m, ValuedAt, "ОсОО «Оценка»");

        set.Should().Throw<DomainException>();
    }

    /// <summary>The appraisal is what backs the issue; the offering price is a separate decision.</summary>
    [Fact]
    public void The_appraisal_does_not_touch_the_offering_economics()
    {
        var property = NewProperty();

        property.SetCollateralAppraisal(1_250_000m, ValuedAt, "ОсОО «Оценка»");

        property.TotalValue.Should().Be(1_000_000m);
        property.TokenPrice.Should().Be(100m);
        property.TotalTokens.Should().Be(10_000);
    }

    [Fact]
    public void An_encumbrance_needs_its_registration_number()
    {
        var property = NewProperty();

        var set = () => property.SetEncumbrance("", ValuedAt);

        set.Should().Throw<DomainException>();
    }

    [Fact]
    public void Registration_details_and_the_collateral_manager_are_recorded()
    {
        var property = NewProperty();
        var manager = Guid.NewGuid();
        var registeredAt = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

        property.SetEncumbrance("ОБР-2026-0042", registeredAt);
        property.SetIssueRegistrationNumber("ВЫП-2026-0007");
        property.SetCollateralManager(manager);

        property.EncumbranceRegistrationNumber.Should().Be("ОБР-2026-0042");
        property.EncumbranceRegisteredAtUtc.Should().Be(registeredAt);
        property.IssueRegistrationNumber.Should().Be("ВЫП-2026-0007");
        property.CollateralManagerUserId.Should().Be(manager);
    }

    [Fact]
    public void Clearing_the_collateral_manager_leaves_no_empty_guid_behind()
    {
        var property = NewProperty();
        property.SetCollateralManager(Guid.NewGuid());

        property.SetCollateralManager(null);
        property.CollateralManagerUserId.Should().BeNull();

        property.SetCollateralManager(Guid.Empty);
        property.CollateralManagerUserId.Should().BeNull();
    }
}
