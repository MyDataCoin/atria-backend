using Atria.Domain.Common;

namespace Atria.Domain.Feedback;

/// <summary>
/// A question left through the feedback form on the public site. Anonymous: the sender has no
/// account, and everything we know about them is what they typed — a name and one way to answer them.
/// </summary>
/// <remarks>
/// This is personal data collected for exactly one purpose (answering the question), which is what
/// the site's consent text promises. Nothing here is linked to a user account, and rows are swept
/// after <see cref="RetentionDays"/> days — a promise that has to be kept by code, not by intention.
/// </remarks>
public sealed class FeedbackRequest : AggregateRoot
{
    public const int MaxFullNameLength = 256;
    public const int MaxContactLength = 256;
    public const int MaxMessageLength = 4000;

    /// <summary>How long a request is kept before the retention sweep deletes it.</summary>
    public const int RetentionDays = 90;

    /// <summary>The name the sender gave. Not verified — it is whatever they typed.</summary>
    public string FullName { get; private set; } = null!;

    /// <summary>Email or phone to answer on. Stored as typed; the form only checks it looks like one of the two.</summary>
    public string Contact { get; private set; } = null!;

    /// <summary>The question itself.</summary>
    public string Message { get; private set; } = null!;

    /// <summary>When an operator marked the request answered. Null while it is still open.</summary>
    public DateTime? HandledAtUtc { get; private set; }

    // private ctor: creation only through the factory method
    private FeedbackRequest() { }

    public static FeedbackRequest Create(string fullName, string contact, string message)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Feedback full name is required.");
        if (string.IsNullOrWhiteSpace(contact))
            throw new DomainException("Feedback contact (email or phone) is required.");
        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException("Feedback message is required.");
        if (fullName.Length > MaxFullNameLength)
            throw new DomainException($"Feedback full name must be at most {MaxFullNameLength} characters.");
        if (contact.Length > MaxContactLength)
            throw new DomainException($"Feedback contact must be at most {MaxContactLength} characters.");
        if (message.Length > MaxMessageLength)
            throw new DomainException($"Feedback message must be at most {MaxMessageLength} characters.");

        return new FeedbackRequest
        {
            Id = Guid.NewGuid(),
            FullName = fullName.Trim(),
            Contact = contact.Trim(),
            Message = message.Trim()
        };
    }

    /// <summary>Marks the request answered (idempotent — the first timestamp is kept).</summary>
    public void MarkHandled(DateTime utcNow) => HandledAtUtc ??= utcNow;
}
