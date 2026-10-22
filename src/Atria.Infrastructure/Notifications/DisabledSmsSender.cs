using Atria.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Notifications;

/// <summary>
/// No-op <see cref="ISmsSender"/>: swallows every SMS instead of handing it to a gateway.
/// </summary>
/// <remarks>
/// <para>
/// TEMPORARY — no OTP delivery provider is wired yet (WhatsApp is coming). This adapter is
/// registered so the OTP and notification paths keep working without any gateway call.
/// </para>
/// <para>
/// The message text is never logged (an OTP body would land in the logs); only the fact that a
/// send was dropped, with the number masked.
/// </para>
/// <para>
/// To enable delivery: register the WhatsApp adapter in
/// <c>DependencyInjection.AddAdapters</c> in place of this one.
/// </para>
/// </remarks>
public sealed class DisabledSmsSender : ISmsSender
{
    private readonly ILogger<DisabledSmsSender> _logger;

    public DisabledSmsSender(ILogger<DisabledSmsSender> logger) => _logger = logger;

    public Task SendAsync(string phoneNumber, string message, CancellationToken ct)
    {
        _logger.LogWarning(
            "SMS delivery is disabled — message to {Phone} dropped (no gateway call was made).",
            Mask(phoneNumber));
        return Task.CompletedTask;
    }

    private static string Mask(string phone)
        => phone.Length <= 7 ? "***" : $"{phone[..4]}***{phone[^3..]}";
}
