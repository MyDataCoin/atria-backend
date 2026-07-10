using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

public sealed class PropertyStateTests
{
    private static Property NewProperty()
        => Property.Create("Bishkek Central", "desc", "addr", 1_000_000m, 1_000m, 1_000, "KGS");

    [Fact]
    public void Create_StartsAsDraft()
    {
        var property = NewProperty();

        property.Status.Should().Be(PropertyStatus.Draft);
    }

    [Fact]
    public void Create_SalesNotPausedByDefault()
    {
        var property = NewProperty();

        property.SalesPaused.Should().BeFalse();
    }

    [Fact]
    public void PauseAndResumeSales_ToggleTheFlag_IndependentOfStatus()
    {
        var property = NewProperty();
        property.Publish(); // Open

        property.PauseSales();
        property.SalesPaused.Should().BeTrue();
        property.Status.Should().Be(PropertyStatus.Open); // status untouched

        property.ResumeSales();
        property.SalesPaused.Should().BeFalse();
        property.Status.Should().Be(PropertyStatus.Open);
    }

    [Fact]
    public void Publish_MovesDraftToOpen()
    {
        var property = NewProperty();

        property.Publish();

        property.Status.Should().Be(PropertyStatus.Open);
    }

    [Fact]
    public void Announce_MovesDraftToComingSoon()
    {
        var property = NewProperty();

        property.Announce();

        property.Status.Should().Be(PropertyStatus.ComingSoon);
    }

    [Fact]
    public void Publish_MovesComingSoonToOpen()
    {
        var property = NewProperty();
        property.Announce();

        property.Publish();

        property.Status.Should().Be(PropertyStatus.Open);
    }

    [Fact]
    public void Announce_WhenAlreadyComingSoon_Throws()
    {
        var property = NewProperty();
        property.Announce();

        var act = () => property.Announce();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Announce_MovesOpenToComingSoon()
    {
        var property = NewProperty();
        property.Publish(); // Draft -> Open

        property.Announce(); // Open -> ComingSoon

        property.Status.Should().Be(PropertyStatus.ComingSoon);
    }

    [Fact]
    public void Unannounce_MovesComingSoonToDraft()
    {
        var property = NewProperty();
        property.Announce(); // Draft -> ComingSoon

        property.Unannounce(); // ComingSoon -> Draft

        property.Status.Should().Be(PropertyStatus.Draft);
    }

    [Fact]
    public void Unannounce_WhenDraft_Throws()
    {
        var property = NewProperty();

        var act = () => property.Unannounce();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Unannounce_WhenOpen_Throws()
    {
        var property = NewProperty();
        property.Publish();

        var act = () => property.Unannounce();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Announce_WhenCompleted_Throws()
    {
        var property = NewProperty();
        property.Publish();
        property.Complete();

        var act = () => property.Announce();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Complete_FromComingSoon_Throws()
    {
        var property = NewProperty();
        property.Announce();

        var act = () => property.Complete();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Complete_MovesOpenToCompleted()
    {
        var property = NewProperty();
        property.Publish();

        property.Complete();

        property.Status.Should().Be(PropertyStatus.Completed);
    }

    [Fact]
    public void Complete_OnDraft_Throws()
    {
        var property = NewProperty();

        var act = () => property.Complete();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Publish_WhenAlreadyOpen_Throws()
    {
        var property = NewProperty();
        property.Publish();

        var act = () => property.Publish();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Publish_WhenCompleted_Throws()
    {
        var property = NewProperty();
        property.Publish();
        property.Complete();

        var act = () => property.Publish();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Complete_WhenAlreadyCompleted_Throws()
    {
        var property = NewProperty();
        property.Publish();
        property.Complete();

        var act = () => property.Complete();

        act.Should().Throw<InvalidStateTransitionException>();
    }
}
