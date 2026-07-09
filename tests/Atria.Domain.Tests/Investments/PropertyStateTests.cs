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
    public void Publish_MovesDraftToOpen()
    {
        var property = NewProperty();

        property.Publish();

        property.Status.Should().Be(PropertyStatus.Open);
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
