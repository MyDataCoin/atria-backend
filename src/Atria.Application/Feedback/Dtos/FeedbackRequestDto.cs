namespace Atria.Application.Feedback.Dtos;

/// <summary>One question from the public-site feedback form, as the panel shows it.</summary>
/// <param name="Id">Request id.</param>
/// <param name="FullName">The name the sender gave (unverified).</param>
/// <param name="Contact">Email or phone to answer on.</param>
/// <param name="Message">The question itself.</param>
/// <param name="CreatedAtUtc">When it was submitted.</param>
/// <param name="HandledAtUtc">When an operator marked it answered; null while it is still open.</param>
public sealed record FeedbackRequestDto(
    Guid Id,
    string FullName,
    string Contact,
    string Message,
    DateTime CreatedAtUtc,
    DateTime? HandledAtUtc);
