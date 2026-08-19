using System.Net;
using System.Net.Mail;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Notifications;

/// <summary>
/// Sends mail through a configured SMTP server. Registered only when <c>Smtp:Host</c> is set;
/// otherwise <see cref="EmailNotificationAdapter"/> (the logging stub) stays in place.
/// </summary>
/// <remarks>
/// The body is never logged: an outgoing mail can carry someone's question along with their name
/// and phone number, and the log is the one place that data has no business being duplicated into.
/// </remarks>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct)
    {
        var from = string.IsNullOrWhiteSpace(_options.From) ? _options.User : _options.From;

        using var message = new MailMessage
        {
            From = new MailAddress(from, _options.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };
        message.To.Add(toEmail);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(_options.User)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.User, _options.Password),
        };

        await client.SendMailAsync(message, ct);
        _logger.LogInformation("Email sent. To={ToEmail} Subject={Subject}", toEmail, subject);
    }
}
