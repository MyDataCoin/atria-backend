namespace Atria.Infrastructure.Configuration;

/// <summary>Where questions from the public site's feedback form are delivered.</summary>
public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    /// <summary>Mailbox the form writes to. Defaults to the address published on the site.</summary>
    public string NotifyEmail { get; init; } = "hello@atria.kg";
}
