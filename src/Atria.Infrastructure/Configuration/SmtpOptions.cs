namespace Atria.Infrastructure.Configuration;

/// <summary>
/// SMTP transport for outgoing mail. Without a <see cref="Host"/> the platform keeps using the
/// logging stub, so a deployment that has not been given a mail server still runs — it just does
/// not send anything, and says so in the log rather than pretending it did.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Mail server host. Empty disables real sending (the log stub stays in place).</summary>
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    /// <summary>STARTTLS / implicit TLS. Leave on for anything but a local relay.</summary>
    public bool UseSsl { get; init; } = true;

    /// <summary>SMTP login. Secret — supply via env (<c>Smtp__User</c>), never in appsettings.</summary>
    public string User { get; init; } = string.Empty;

    /// <summary>SMTP password. Secret — supply via env (<c>Smtp__Password</c>).</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Envelope sender. Falls back to <see cref="User"/> when empty.</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>Display name shown next to the sender address.</summary>
    public string FromName { get; init; } = "ATRIA";
}
