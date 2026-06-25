using Atria.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Notifications;

/// <summary>
/// <see cref="IEmailSender"/> log-based stub. Emits a structured log line (recipient
/// address + subject only — no body PII) so the platform is functional without a live
/// provider. Swap the body of <see cref="SendAsync"/> for a real SMTP / SES / provider
/// client; the public surface stays the same.
/// </summary>
public sealed class EmailNotificationAdapter : IEmailSender
{
    private readonly ILogger<EmailNotificationAdapter> _logger;

    public EmailNotificationAdapter(ILogger<EmailNotificationAdapter> logger)
        => _logger = logger;

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct)
    {
        // NOTE: real provider call goes here (e.g. SmtpClient / AWS SES SendEmailAsync).
        // Body intentionally excluded from logs to avoid leaking PII.
        _logger.LogInformation(
            "Email dispatched (stub). To={ToEmail} Subject={Subject} BodyLength={BodyLength}",
            toEmail, subject, body.Length);

        return Task.CompletedTask;
    }
}
