using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// The building cover of an object still under construction is a render. The site shows the first
/// building image as the cover, so the image carries what it is and the order is kept explicitly.
/// </summary>
public sealed class BuildingGalleryTests
{
    private static Building NewBuilding() => Building.Create("Офисы 7 мкр. д.41а");

    [Fact]
    public void AnImageIsAPhotoUnlessItSaysOtherwise()
    {
        var image = NewBuilding().AddImage("/media/images/a.jpg");

        image.Kind.Should().Be(PropertyImageKind.Photo);
        image.Caption.Should().BeNull();
    }

    [Fact]
    public void ARenderKeepsItsKindAndTrimmedCaption()
    {
        var image = NewBuilding().AddImage(
            "/media/images/render.jpg", PropertyImageKind.Render, "  Визуализация  ");

        image.Kind.Should().Be(PropertyImageKind.Render);
        image.Caption.Should().Be("Визуализация");
    }

    [Fact]
    public void ACaptionLongerThanTheLimitIsRefused()
    {
        var act = () => NewBuilding().AddImage(
            "/media/images/a.jpg", PropertyImageKind.Render, new string('я', PropertyImage.MaxCaption + 1));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void TheFirstUploadStaysTheCoverAndANewcomerAfterARemovalGoesLast()
    {
        var building = NewBuilding();
        var cover = building.AddImage("/media/images/cover.jpg", PropertyImageKind.Render);
        var middle = building.AddImage("/media/images/b.jpg");
        var last = building.AddImage("/media/images/c.jpg");

        building.RemoveImage(middle.Id);
        var newcomer = building.AddImage("/media/images/d.jpg");

        building.Images.Select(i => i.Id).Should().Equal(cover.Id, last.Id, newcomer.Id);
        newcomer.SortOrder.Should().BeGreaterThan(last.SortOrder);
    }
}
