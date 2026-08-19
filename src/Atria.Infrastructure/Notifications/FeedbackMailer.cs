using System.Globalization;
using Atria.Application.Abstractions;
using Atria.Domain.Feedback;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Notifications;

/// <summary>
/// Mails a feedback request to the address in <see cref="FeedbackOptions.NotifyEmail"/>
/// (<c>hello@atria.kg</c> unless configured otherwise).
/// </summary>
/// <remarks>
/// The letter carries the sender's contact in the body and not in Reply-To, because the contact may
/// well be a phone number — parsing it into a mail header would break on exactly the people who left
/// one. Whoever answers reads it and picks the channel.
/// </remarks>
public sealed class FeedbackMailer : IFeedbackMailer
{
    private readonly IEmailSender _email;
    private readonly FeedbackOptions _options;

    public FeedbackMailer(IEmailSender email, IOptions<FeedbackOptions> options)
    {
        _email = email;
        _options = options.Value;
    }

    public Task NotifyAsync(FeedbackRequest feedback, CancellationToken ct)
    {
        var subject = $"Обратная связь с сайта — {feedback.FullName}";

        var body =
            $"""
             Новое обращение через форму обратной связи на atria.kg.

             Имя:     {feedback.FullName}
             Контакт: {feedback.Contact}
             Дата:    {feedback.CreatedAtUtc.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)} UTC

             Вопрос:
             {feedback.Message}

             ---
             Обращение также видно в админ-панели (Поддержка → Обратная связь).
             Через 90 дней запись удаляется автоматически.
             """;

        return _email.SendAsync(_options.NotifyEmail, subject, body, ct);
    }
}
