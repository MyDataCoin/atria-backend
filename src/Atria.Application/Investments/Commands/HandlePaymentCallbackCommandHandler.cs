using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Investments;

namespace Atria.Application.Investments.Commands;

/// <summary>
/// Verifies and applies a payment provider callback. Idempotent and exactly-once:
/// the parsed provider event id is recorded in <see cref="IProcessedEventStore"/> so a
/// redelivered webhook never moves money twice.
/// </summary>
public sealed class HandlePaymentCallbackCommandHandler
    : IRequestHandler<HandlePaymentCallbackCommand, Result>
{
    private readonly IEnumerable<IPaymentProviderStrategy> _providers;
    private readonly IInvestmentRepository _investments;
    private readonly IProcessedEventStore _processedEvents;
    private readonly IUnitOfWork _unitOfWork;

    public HandlePaymentCallbackCommandHandler(
        IEnumerable<IPaymentProviderStrategy> providers,
        IInvestmentRepository investments,
        IProcessedEventStore processedEvents,
        IUnitOfWork unitOfWork)
    {
        _providers = providers;
        _investments = investments;
        _processedEvents = processedEvents;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(HandlePaymentCallbackCommand request, CancellationToken ct)
    {
        // Pick the Strategy by provider name (matched against the ProviderType enum).
        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderType.ToString(), request.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
            return Result.Failure(
                Error.Validation("payment.providerUnsupported", "Unknown payment provider."));

        if (!provider.VerifySignature(request.Payload))
            return Result.Failure(
                Error.Unauthorized("payment.signatureInvalid", "Webhook signature verification failed."));

        var callback = provider.ParseCallback(request.Payload);

        // Exactly-once guard keyed on the provider's stable event id.
        var key = IdempotencyKey.For(this, callback.EventId);
        if (await _processedEvents.IsProcessedAsync(key, ct))
            return Result.Success();

        var investment = await _investments.GetByIdAsync(callback.InvestmentId, ct);
        if (investment is null)
            return Result.Failure(
                Error.NotFound("investment.notFound", "Referenced investment does not exist."));

        switch (callback.Decision)
        {
            case PaymentDecision.Completed:
                // Reconcile what was paid against what was owed. A signed webhook is
                // authentic but its amount is provider/back-office supplied, so never
                // activate an investment for an under/over/wrong-currency payment —
                // record it as failed instead of silently confirming.
                if (callback.Amount != investment.Amount ||
                    !string.Equals(callback.Currency, investment.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    investment.FailPayment(
                        provider.ProviderType,
                        $"Payment amount/currency mismatch: paid {callback.Amount} {callback.Currency}, " +
                        $"owed {investment.Amount} {investment.Currency}.");
                    break;
                }

                investment.ConfirmPayment(
                    provider.ProviderType, callback.ExternalPaymentId, callback.Amount, callback.Currency);
                break;
            case PaymentDecision.Failed:
                investment.FailPayment(
                    provider.ProviderType, callback.FailureReason ?? "Payment failed.");
                break;
            default:
                return Result.Failure(
                    Error.Validation("payment.decisionUnknown", "Unrecognized payment decision."));
        }

        _investments.Update(investment);
        await _processedEvents.MarkProcessedAsync(key, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
