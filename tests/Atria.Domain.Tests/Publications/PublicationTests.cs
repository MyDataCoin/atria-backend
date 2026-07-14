using System.Linq;
using Atria.Domain.Common;
using Atria.Domain.Publications;
using Atria.Domain.Publications.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Publications;

public sealed class PublicationTests
{
    private static readonly DateTime Now = new(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private static Publication NewPublication(Guid? propertyId = null)
        => Publication.Publish(
            PublicationType.FinancialReport, "Квартальный отчёт Q2 2026", "Полный текст…",
            propertyId, Author, Now);

    [Fact]
    public void Publish_CreatesPublishedItem_AndRaisesEvent()
    {
        var propertyId = Guid.NewGuid();

        var publication = NewPublication(propertyId);

        publication.Status.Should().Be(PublicationStatus.Published);
        publication.Type.Should().Be(PublicationType.FinancialReport);
        publication.PropertyId.Should().Be(propertyId);
        publication.AuthorId.Should().Be(Author);
        publication.PublishedAtUtc.Should().Be(Now);
        publication.DomainEvents.OfType<PublicationPublishedEvent>().Should().ContainSingle()
            .Which.PropertyId.Should().Be(propertyId);
    }

    [Fact]
    public void Publish_AllowsNullProperty_ForGeneralNews()
    {
        var publication = NewPublication(propertyId: null);

        publication.PropertyId.Should().BeNull();
        publication.DomainEvents.OfType<PublicationPublishedEvent>().Single()
            .PropertyId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Publish_WhenTitleMissing_Throws(string title)
    {
        var act = () => Publication.Publish(
            PublicationType.GeneralNews, title, "body", null, Author, Now);

        act.Should().Throw<DomainException>().WithMessage("*title is required*");
    }

    [Fact]
    public void Publish_WhenBodyTooLong_Throws()
    {
        var body = new string('x', Publication.MaxBodyLength + 1);

        var act = () => Publication.Publish(
            PublicationType.GeneralNews, "Title", body, null, Author, Now);

        act.Should().Throw<DomainException>().WithMessage("*body cannot exceed*");
    }

    [Fact]
    public void Update_ChangesOnlySuppliedFields_AndRaisesNoNewEvent()
    {
        var publication = NewPublication();
        publication.ClearEvents();

        publication.Update(type: PublicationType.NewsRelease, title: "Исправленный заголовок", body: null);

        publication.Type.Should().Be(PublicationType.NewsRelease);
        publication.Title.Should().Be("Исправленный заголовок");
        publication.Body.Should().Be("Полный текст…", "a null body must leave the text untouched");
        publication.DomainEvents.Should().BeEmpty("an edit must not re-notify readers");
    }

    [Fact]
    public void Update_WhenNewTitleBlank_Throws()
    {
        var publication = NewPublication();

        var act = () => publication.Update(null, "  ", null);

        act.Should().Throw<DomainException>().WithMessage("*title is required*");
    }
}
