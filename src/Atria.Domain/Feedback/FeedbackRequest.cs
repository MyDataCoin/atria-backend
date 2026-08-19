using Atria.Domain.Common;

namespace Atria.Domain.Feedback;

/// <summary>
/// A question left through the feedback form on the public site. Anonymous: the sender has no
/// account, and everything we know about them is what they typed — a name and two ways to reach them.
/// </summary>
/// <remarks>
/// This is personal data collected for exactly one purpose (answering the question), which is what
/// the site's consent text promises. Nothing here is linked to a user account, and rows are swept
/// after <see cref="RetentionDays"/> days — a promise that has to be kept by code, not by intention.
/// </remarks>
public sealed class FeedbackRequest : AggregateRoot
{
    public const int MaxFullNameLength = 256;
    public const int MaxEmailLength = 256;
    public const int MaxPhoneLength = 32;
    public const int MaxMessageLength = 4000;

    /// <summary>How long a request is kept before the retention sweep deletes it.</summary>
    public const int RetentionDays = 90;

    /// <summary>The name the sender gave. Not verified — it is whatever they typed.</summary>
    public string FullName { get; private set; } = null!;

    /// <summary>Email to answer on. Stored as typed.</summary>
    public string Email { get; private set; } = null!;

    /// <summary>Phone to answer on. Stored as typed — formatting is the sender's business.</summary>
    public string Phone { get; private set; } = null!;

    /// <summary>The question itself.</summary>
    public string Message { get; private set; } = null!;

    /// <summary>When an operator marked the request answered. Null while it is still open.</summary>
    public DateTime? HandledAtUtc { get; private set; }

    // private ctor: creation only through the factory method
    private FeedbackRequest() { }

    public static FeedbackRequest Create(string fullName, string email, string phone, string message)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Feedback full name is required.");
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Feedback email is required.");
        if (string.IsNullOrWhiteSpace(phone))
            throw new DomainException("Feedback phone is required.");
        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException("Feedback message is required.");
        if (fullName.Length > MaxFullNameLength)
            throw new DomainException($"Feedback full name must be at most {MaxFullNameLength} characters.");
        if (email.Length > MaxEmailLength)
            throw new DomainException($"Feedback email must be at most {MaxEmailLength} characters.");
        if (phone.Length > MaxPhoneLength)
            throw new DomainException($"Feedback phone must be at most {MaxPhoneLength} characters.");
        if (message.Length > MaxMessageLength)
            throw new DomainException($"Feedback message must be at most {MaxMessageLength} characters.");

        return new FeedbackRequest
        {
            Id = Guid.NewGuid(),
            FullName = fullName.Trim(),
            Email = email.Trim(),
            Phone = phone.Trim(),
            Message = message.Trim()
        };
    }

    /// <summary>Marks the request answered (idempotent — the first timestamp is kept).</summary>
    public void MarkHandled(DateTime utcNow) => HandledAtUtc ??= utcNow;
}
