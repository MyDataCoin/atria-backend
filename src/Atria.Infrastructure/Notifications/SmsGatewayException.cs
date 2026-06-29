namespace Atria.Infrastructure.Notifications;

/// <summary>
/// Raised when the SMS gateway cannot accept a message: either the transport failed
/// (HTTP error / unreachable) or the gateway replied with a non-zero status (e.g.
/// smspro 2 = bad credentials, 3 = IP not whitelisted, 5 = unknown sender).
/// Carries the raw gateway status (when known) so callers can surface a precise,
/// non-secret reason instead of a generic 500.
/// </summary>
public sealed class SmsGatewayException : Exception
{
    /// <summary>The gateway's numeric status code, or <c>null</c> for a transport/HTTP failure.</summary>
    public int? GatewayStatus { get; }

    public SmsGatewayException(int? gatewayStatus, string message) : base(message)
        => GatewayStatus = gatewayStatus;

    public SmsGatewayException(int? gatewayStatus, string message, Exception innerException)
        : base(message, innerException)
        => GatewayStatus = gatewayStatus;
}