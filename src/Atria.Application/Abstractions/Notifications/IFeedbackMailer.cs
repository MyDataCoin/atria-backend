using Atria.Domain.Feedback;

namespace Atria.Application.Abstractions;

/// <summary>
/// Delivers a question from the public site's feedback form to the mailbox that answers it.
/// </summary>
/// <remarks>
/// Its own abstraction rather than a direct <see cref="IEmailSender"/> call, because the recipient
/// address and the wording of the letter are deployment configuration, not application logic.
/// </remarks>
public interface IFeedbackMailer
{
    Task NotifyAsync(FeedbackRequest feedback, CancellationToken ct);
}
