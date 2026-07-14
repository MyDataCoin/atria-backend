using Atria.Domain.Common;
using Atria.Domain.Publications.Events;

namespace Atria.Domain.Publications;

/// <summary>
/// A news-feed publication written by an admin (financial report, news release, valuation audit, or
/// general news). Optionally tied to a property — a null <see cref="PropertyId"/> means a general
/// platform-wide item. Published items are readable by investors; drafts stay admin-only.
/// </summary>
public sealed class Publication : AggregateRoot
{
    /// <summary>Maximum length of a publication title.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Maximum length of a publication body (a report can run to a few thousand characters).</summary>
    public const int MaxBodyLength = 10_000;

    public PublicationType Type { get; private set; }
    public string Title { get; private set; } = null!;

    /// <summary>Plain text; newlines are preserved verbatim.</summary>
    public string Body { get; private set; } = null!;

    /// <summary>The property this item is about; <c>null</c> for a general platform news item.</summary>
    public Guid? PropertyId { get; private set; }

    public PublicationStatus Status { get; private set; }

    /// <summary>When the item went live. Set on publication.</summary>
    public DateTime PublishedAtUtc { get; private set; }

    /// <summary>The admin who published it.</summary>
    public Guid AuthorId { get; private set; }

    // private ctor: creation only through the factory method
    private Publication() { }

    /// <summary>
    /// Creates an already-published item (the first iteration publishes straight away) and raises
    /// <see cref="PublicationPublishedEvent"/> so readers get notified.
    /// </summary>
    public static Publication Publish(
        PublicationType type, string title, string body, Guid? propertyId, Guid authorId, DateTime nowUtc)
    {
        Validate(title, body);
        if (authorId == Guid.Empty)
            throw new DomainException("Publication author is required.");

        var publication = new Publication
        {
            Id = Guid.NewGuid(),
            Type = type,
            Title = title,
            Body = body,
            PropertyId = propertyId,
            Status = PublicationStatus.Published,
            PublishedAtUtc = nowUtc,
            AuthorId = authorId
        };

        publication.RaiseEvent(new PublicationPublishedEvent(publication.Id, propertyId, title));
        return publication;
    }

    /// <summary>
    /// Edits the copy of an existing item (fixing a typo in an already-sent report). Only the
    /// content changes — the property link, author and publication time stay put, and no new
    /// notification is raised. Null arguments leave the corresponding field untouched.
    /// </summary>
    public void Update(PublicationType? type, string? title, string? body)
    {
        var newTitle = title ?? Title;
        var newBody = body ?? Body;
        Validate(newTitle, newBody);

        Type = type ?? Type;
        Title = newTitle;
        Body = newBody;
    }

    private static void Validate(string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Publication title is required.");
        if (title.Length > MaxTitleLength)
            throw new DomainException($"Publication title cannot exceed {MaxTitleLength} characters.");
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("Publication body is required.");
        if (body.Length > MaxBodyLength)
            throw new DomainException($"Publication body cannot exceed {MaxBodyLength} characters.");
    }
}
