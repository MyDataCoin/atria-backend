using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// The gallery of an object nobody can photograph. The first issue is a land plot under design and
/// the management company is supplying renders from the project documentation, so an image carries
/// what it actually is — and the cover is chosen rather than being whichever upload finished first.
/// </summary>
public sealed class PropertyGalleryTests
{
    private static Property NewIssue() => Property.Create(
        "ЖК на Токомбаева", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");

    [Fact]
    public void AnImageIsAPhotoUnlessItSaysOtherwise()
    {
        // What every image uploaded before the kind existed was.
        var issue = NewIssue();

        var image = issue.AddImage("/media/images/a.jpg");

        image.Kind.Should().Be(PropertyImageKind.Photo);
        image.Caption.Should().BeNull();
    }

    [Fact]
    public void ARenderKeepsItsKindAndCaption()
    {
        var issue = NewIssue();

        var image = issue.AddImage(
            "/media/images/render.jpg", PropertyImageKind.Render, "Визуализация, вид с юга");

        image.Kind.Should().Be(PropertyImageKind.Render);
        image.Caption.Should().Be("Визуализация, вид с юга");
    }

    [Fact]
    public void ABlankCaptionIsStoredAsAbsent()
    {
        var issue = NewIssue();

        issue.AddImage("/media/images/a.jpg", PropertyImageKind.Render, "   ")
            .Caption.Should().BeNull();
    }

    [Fact]
    public void ImagesComeBackInGalleryOrder()
    {
        var issue = NewIssue();
        var first = issue.AddImage("/media/images/1.jpg");
        var second = issue.AddImage("/media/images/2.jpg");

        issue.Images.Select(i => i.Id).Should().ContainInOrder(first.Id, second.Id);
    }

    [Fact]
    public void ANewImageNeverLandsOnATakenPosition()
    {
        // A gallery whose middle image was removed has a gap, and counting the remainder would hand
        // the newcomer a position another image already holds.
        var issue = NewIssue();
        issue.AddImage("/media/images/1.jpg");
        var middle = issue.AddImage("/media/images/2.jpg");
        issue.AddImage("/media/images/3.jpg");

        issue.RemoveImage(middle.Id);
        issue.AddImage("/media/images/4.jpg");

        issue.Images.Select(i => i.SortOrder).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ReorderImages_makesTheFirstIdTheCover()
    {
        var issue = NewIssue();
        var a = issue.AddImage("/media/images/a.jpg");
        var b = issue.AddImage("/media/images/b.jpg");
        var c = issue.AddImage("/media/images/c.jpg");

        issue.ReorderImages([c.Id, a.Id, b.Id]);

        issue.Images.Select(i => i.Id).Should().ContainInOrder(c.Id, a.Id, b.Id);
        issue.Images.First().Id.Should().Be(c.Id);
    }

    [Fact]
    public void ReorderImages_refusesAPartialOrder()
    {
        // The unnamed images would keep positions the named ones now also claim, and which of them
        // ended up on the card would come down to how the list was sorted afterwards.
        var issue = NewIssue();
        var a = issue.AddImage("/media/images/a.jpg");
        issue.AddImage("/media/images/b.jpg");

        var act = () => issue.ReorderImages([a.Id]);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReorderImages_refusesARepeatedId()
    {
        var issue = NewIssue();
        var a = issue.AddImage("/media/images/a.jpg");
        issue.AddImage("/media/images/b.jpg");

        var act = () => issue.ReorderImages([a.Id, a.Id]);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReorderImages_refusesAnImageFromSomewhereElse()
    {
        var issue = NewIssue();
        var a = issue.AddImage("/media/images/a.jpg");

        var act = () => issue.ReorderImages([Guid.NewGuid()]);

        act.Should().Throw<DomainException>();
        issue.Images.Single().Id.Should().Be(a.Id);
    }

    [Fact]
    public void ACaptionCannotRunPastItsLimit()
    {
        var issue = NewIssue();

        var act = () => issue.AddImage(
            "/media/images/a.jpg", PropertyImageKind.Render, new string('я', PropertyImage.MaxCaption + 1));

        act.Should().Throw<DomainException>();
    }
}
