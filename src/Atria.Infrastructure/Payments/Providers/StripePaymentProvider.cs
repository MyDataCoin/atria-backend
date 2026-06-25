using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Atria.Infrastructure.Payments.Providers;

/// <summary>
/// Stripe payment provider (Stripe.net). Creates a hosted Checkout Session, verifies
/// the Stripe-Signature header via EventUtility (HMAC + timestamp tolerance), and
/// parses Checkout/PaymentIntent webhooks into a decision. Selected through DI by
/// <see cref="ProviderType"/> (Strategy).
/// </summary>
public sealed class StripePaymentProvider : IPaymentProviderStrategy
{
    private const string SignatureHeader = "Stripe-Signature";

    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentProvider> _logger;
    private readonly StripeClient _client; // instance client — no global static config

    public StripePaymentProvider(IOptions<StripeOptions> options, ILogger<StripePaymentProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new StripeClient(_options.ApiKey);
    }

    public PaymentProviderType ProviderType => PaymentProviderType.Stripe;

    public async Task<PaymentSessionResult> CreateSessionAsync(PaymentRequest request, CancellationToken ct)
    {
        var currency = string.IsNullOrWhiteSpace(request.Currency)
            ? _options.DefaultCurrency
            : request.Currency.ToLowerInvariant();

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            // Correlate the webhook back to our Investment without trusting the body fields.
            ClientReferenceId = request.InvestmentId.ToString(),
            SuccessUrl = request.ReturnUrl,
            CancelUrl = request.ReturnUrl,
            Metadata = new Dictionary<string, string>
            {
                ["investmentId"] = request.InvestmentId.ToString(),
                ["investorId"] = request.InvestorId.ToString()
            },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = currency,
                        // Stripe expects the smallest currency unit (cents).
                        UnitAmount = (long)decimal.Round(request.Amount * 100m, 0),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Investment {request.InvestmentId}"
                        }
                    }
                }
            },
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["investmentId"] = request.InvestmentId.ToString()
                }
            }
        };

        var service = new SessionService(_client);
        var session = await service.CreateAsync(options, cancellationToken: ct);

        return new PaymentSessionResult(session.Id, session.Url);
    }

    public bool VerifySignature(WebhookPayload payload)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            _logger.LogWarning("Stripe webhook secret not configured; rejecting webhook.");
            return false;
        }

        var signature = payload.Signature ?? Header(payload, SignatureHeader);
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        try
        {
            // EventUtility validates the HMAC AND the timestamp tolerance (replay protection),
            // throwing StripeException on any mismatch — never trust the body otherwise.
            EventUtility.ConstructEvent(
                payload.RawBody,
                signature,
                _options.WebhookSecret,
                _options.WebhookToleranceSeconds);
            return true;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return false;
        }
    }

    public PaymentCallbackResult ParseCallback(WebhookPayload payload)
    {
        // Re-parse via EventUtility so we operate only on the verified, typed object.
        var signature = payload.Signature ?? Header(payload, SignatureHeader) ?? string.Empty;
        var stripeEvent = EventUtility.ConstructEvent(
            payload.RawBody,
            signature,
            _options.WebhookSecret,
            _options.WebhookToleranceSeconds);

        return stripeEvent.Type switch
        {
            "checkout.session.completed" or "checkout.session.async_payment_succeeded"
                => FromSession(stripeEvent, PaymentDecision.Completed),
            "checkout.session.async_payment_failed" or "checkout.session.expired"
                => FromSession(stripeEvent, PaymentDecision.Failed),
            "payment_intent.succeeded"
                => FromPaymentIntent(stripeEvent, PaymentDecision.Completed),
            "payment_intent.payment_failed" or "payment_intent.canceled"
                => FromPaymentIntent(stripeEvent, PaymentDecision.Failed),
            _ => throw new InvalidOperationException($"Unhandled Stripe event type '{stripeEvent.Type}'.")
        };
    }

    private static PaymentCallbackResult FromSession(Event stripeEvent, PaymentDecision decision)
    {
        var session = (Session)stripeEvent.Data.Object;
        var investmentId = ResolveInvestmentId(session.ClientReferenceId, session.Metadata);
        var amount = ((session.AmountTotal ?? 0L) / 100m);
        var currency = session.Currency ?? string.Empty;
        var externalId = session.PaymentIntentId ?? session.Id;
        var reason = decision == PaymentDecision.Failed ? "Stripe checkout not completed." : null;

        return new PaymentCallbackResult(
            externalId, investmentId, decision, amount, currency, reason, stripeEvent.Id);
    }

    private static PaymentCallbackResult FromPaymentIntent(Event stripeEvent, PaymentDecision decision)
    {
        var intent = (PaymentIntent)stripeEvent.Data.Object;
        var investmentId = ResolveInvestmentId(null, intent.Metadata);
        var amount = intent.Amount / 100m;
        var currency = intent.Currency ?? string.Empty;
        var reason = decision == PaymentDecision.Failed
            ? intent.LastPaymentError?.Message ?? "Stripe payment failed."
            : null;

        return new PaymentCallbackResult(
            intent.Id, investmentId, decision, amount, currency, reason, stripeEvent.Id);
    }

    private static Guid ResolveInvestmentId(string? clientReferenceId, IDictionary<string, string>? metadata)
    {
        if (Guid.TryParse(clientReferenceId, out var fromRef))
            return fromRef;

        if (metadata is not null &&
            metadata.TryGetValue("investmentId", out var raw) &&
            Guid.TryParse(raw, out var fromMeta))
            return fromMeta;

        throw new InvalidOperationException("Stripe webhook did not carry a resolvable investment id.");
    }

    private static string? Header(WebhookPayload payload, string name)
        => payload.Headers.TryGetValue(name, out var v) ? v : null;
}
