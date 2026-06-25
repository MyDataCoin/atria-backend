using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Payments.Providers;

/// <summary>
/// Manual bank-transfer provider. The "session" returns static bank details plus a
/// per-investment payment reference (encoded as JSON in the URL field, since there is
/// no hosted page). Settlement is confirmed by a back-office webhook signed with an
/// HMAC-SHA256 secret (timestamp-checked for replay protection). Selected through DI
/// by <see cref="ProviderType"/> (Strategy).
/// </summary>
public sealed class BankTransferPaymentProvider : IPaymentProviderStrategy
{
    private const string SignatureHeader = "x-signature";
    private const string TimestampHeader = "x-timestamp";

    private readonly BankTransferOptions _options;
    private readonly ILogger<BankTransferPaymentProvider> _logger;

    public BankTransferPaymentProvider(
        IOptions<BankTransferOptions> options,
        ILogger<BankTransferPaymentProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public PaymentProviderType ProviderType => PaymentProviderType.BankTransfer;

    public Task<PaymentSessionResult> CreateSessionAsync(PaymentRequest request, CancellationToken ct)
    {
        // The investor references this id on their wire so back-office can reconcile.
        var reference = $"ATRIA-{request.InvestmentId:N}";

        // No hosted page: surface bank details + reference as a JSON payload in PaymentUrl.
        var details = JsonSerializer.Serialize(new
        {
            beneficiary = _options.BeneficiaryName,
            iban = _options.Iban,
            bic = _options.Bic,
            bank = _options.BankName,
            reference,
            amount = request.Amount,
            currency = request.Currency
        });

        return Task.FromResult(new PaymentSessionResult(reference, details));
    }

    public bool VerifySignature(WebhookPayload payload)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            _logger.LogWarning("BankTransfer webhook secret not configured; rejecting webhook.");
            return false;
        }

        var signature = payload.Signature ?? Header(payload, SignatureHeader);
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        // Replay protection: require a fresh timestamp.
        if (!TryGetTimestamp(payload, out var sentAt))
            return false;

        if (Math.Abs((DateTimeOffset.UtcNow - sentAt).TotalSeconds) > _options.WebhookToleranceSeconds)
        {
            _logger.LogWarning("BankTransfer webhook timestamp outside tolerance; possible replay.");
            return false;
        }

        var expected = ComputeHmacHex(payload.RawBody, _options.WebhookSecret);
        return FixedTimeEqualsHex(expected, signature.Trim());
    }

    public PaymentCallbackResult ParseCallback(WebhookPayload payload)
    {
        using var doc = JsonDocument.Parse(payload.RawBody);
        var root = doc.RootElement;

        if (!root.TryGetProperty("investmentId", out var idProp) ||
            !Guid.TryParse(idProp.GetString(), out var investmentId))
            throw new InvalidOperationException("BankTransfer webhook missing investmentId.");

        var confirmed = root.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;
        var amount = root.TryGetProperty("amount", out var amt) && amt.TryGetDecimal(out var a) ? a : 0m;
        var currency = GetString(root, "currency") ?? string.Empty;
        var externalId = GetString(root, "reference", "externalPaymentId") ?? investmentId.ToString("N");
        var eventId = GetString(root, "eventId") ?? externalId;
        var reason = confirmed ? null : GetString(root, "reason") ?? "Bank transfer not confirmed.";

        var decision = confirmed ? PaymentDecision.Completed : PaymentDecision.Failed;
        return new PaymentCallbackResult(externalId, investmentId, decision, amount, currency, reason, eventId);
    }

    private static bool TryGetTimestamp(WebhookPayload payload, out DateTimeOffset timestamp)
    {
        if (payload.Timestamp is { } t)
        {
            timestamp = t;
            return true;
        }

        if (long.TryParse(Header(payload, TimestampHeader), out var unix))
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unix);
            return true;
        }

        timestamp = default;
        return false;
    }

    private static string ComputeHmacHex(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private static bool FixedTimeEqualsHex(string expectedHex, string providedHex)
    {
        var idx = providedHex.IndexOf('=');
        if (idx >= 0 && idx < providedHex.Length - 1)
            providedHex = providedHex[(idx + 1)..];

        byte[] expected;
        byte[] provided;
        try
        {
            expected = Convert.FromHexString(expectedHex);
            provided = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private static string? Header(WebhookPayload payload, string name)
        => payload.Headers.TryGetValue(name, out var v) ? v : null;

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }
}
