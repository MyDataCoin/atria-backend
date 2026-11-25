using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Atria.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Notifications;

/// <summary>Delivers a one-time login code to a phone. The code is never logged.</summary>
/// <remarks>
/// Separate from <see cref="ISmsSender"/> on purpose: code-only channels (Telegram Gateway, WhatsApp
/// authentication templates) take the code itself and render their own message — they cannot carry
/// arbitrary text, so they could never stand behind a general-purpose message sender.
/// </remarks>
public interface IOtpCodeSender
{
    Task SendCodeAsync(string phone, string code, int ttlMinutes, CancellationToken ct);
}

/// <summary>The code as a text message through <see cref="ISmsSender"/> — the fallback when no code channel is configured.</summary>
public sealed class SmsOtpCodeSender : IOtpCodeSender
{
    private readonly ISmsSender _sms;

    public SmsOtpCodeSender(ISmsSender sms) => _sms = sms;

    public Task SendCodeAsync(string phone, string code, int ttlMinutes, CancellationToken ct)
        => _sms.SendAsync(
            phone,
            $"Добро пожаловать в Atria! Ваш код подтверждения: {code}. Он истечёт через {ttlMinutes} минут.",
            ct);
}

/// <summary>Telegram Gateway settings (section <c>TelegramGateway</c>). Empty token = not used.</summary>
public sealed class TelegramGatewayOptions
{
    public const string SectionName = "TelegramGateway";

    /// <summary>API token from gateway.telegram.org. Secret: set via environment, never in a repo.</summary>
    public string? ApiToken { get; init; }

    public string BaseUrl { get; init; } = "https://gatewayapi.telegram.org";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiToken);
}

/// <summary>
/// Sends the login code through Telegram's official Verification Codes service: the person receives
/// it in Telegram from the verified "Verification Codes" account. No business verification, no SMS
/// sender registration; billed per delivered code.
/// </summary>
public sealed class TelegramGatewayOtpSender : IOtpCodeSender
{
    private readonly HttpClient _http;
    private readonly TelegramGatewayOptions _options;
    private readonly ILogger<TelegramGatewayOtpSender> _logger;

    public TelegramGatewayOtpSender(
        HttpClient http, IOptions<TelegramGatewayOptions> options, ILogger<TelegramGatewayOtpSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendCodeAsync(string phone, string code, int ttlMinutes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/sendVerificationMessage")
        {
            Content = JsonContent.Create(new SendVerificationMessageRequest(
                PhoneNumber: phone.StartsWith('+') ? phone : $"+{phone}",
                Code: code,
                // The gateway accepts 30–3600 s; past it the message says the code expired.
                Ttl: Math.Clamp(ttlMinutes * 60, 30, 3600)))
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new SmsGatewayException(null, "Telegram Gateway is unreachable.", ex);
        }

        using (response)
        {
            GatewayResponse? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync<GatewayResponse>(cancellationToken: ct);
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON (proxy error page): the status code below says enough.
            }

            if (!response.IsSuccessStatusCode || body is not { Ok: true })
            {
                // The error is a code such as PHONE_NUMBER_INVALID or BALANCE_NOT_ENOUGH — no secrets in it.
                _logger.LogWarning(
                    "Telegram Gateway refused a code: HTTP {Status}, error {Error}.",
                    (int)response.StatusCode, body?.Error ?? "<none>");
                throw new SmsGatewayException((int)response.StatusCode, $"Telegram Gateway: {body?.Error ?? "request failed"}");
            }
        }
    }

    private sealed record SendVerificationMessageRequest(
        [property: JsonPropertyName("phone_number")] string PhoneNumber,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("ttl")] int Ttl);

    private sealed class GatewayResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
