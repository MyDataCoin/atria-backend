using Atria.Domain.Investments;

namespace Atria.Application.Abstractions;

public sealed record PaymentRequest(
    Guid InvestmentId,
    Guid InvestorId,
    decimal Amount,
    string Currency,
    string? ReturnUrl);

/// <summary>Hosted/redirect payment session.</summary>
public sealed record PaymentSessionResult(string SessionId, string? PaymentUrl);

public enum PaymentDecision { Completed, Failed }

/// <summary>Parsed result of a payment provider webhook callback.</summary>
public sealed record PaymentCallbackResult(
    string ExternalPaymentId,
    Guid InvestmentId,
    PaymentDecision Decision,
    decimal Amount,
    string Currency,
    string? FailureReason,
    string EventId);

/// <summary>
/// Payment provider Strategy. Async by design (create session, then webhook).
/// Selected by ProviderType through DI.
/// </summary>
public interface IPaymentProviderStrategy
{
    PaymentProviderType ProviderType { get; }

    Task<PaymentSessionResult> CreateSessionAsync(PaymentRequest request, CancellationToken ct);

    bool VerifySignature(WebhookPayload payload);

    PaymentCallbackResult ParseCallback(WebhookPayload payload);
}
