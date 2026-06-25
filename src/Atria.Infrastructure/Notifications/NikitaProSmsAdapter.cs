using System.Text;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Notifications;

/// <summary>
/// <see cref="ISmsSender"/> backed by the Nikita Pro SMS gateway. Used for OTP and
/// notification SMS. The HttpClient is injected (configured via IHttpClientFactory);
/// gateway credentials come from <see cref="NikitaProOptions"/>.
/// </summary>
public sealed class NikitaProSmsAdapter : ISmsSender
{
    private readonly HttpClient _http;
    private readonly NikitaProOptions _options;
    private readonly ILogger<NikitaProSmsAdapter> _logger;

    public NikitaProSmsAdapter(
        HttpClient http,
        IOptions<NikitaProOptions> options,
        ILogger<NikitaProSmsAdapter> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string phoneNumber, string message, CancellationToken ct)
    {
        // NOTE: Nikita Pro's "smspro" API accepts an XML POST body to <BaseUrl>/message/
        // with the following shape (transaction id is an arbitrary unique request id):
        //   <?xml version="1.0" encoding="UTF-8"?>
        //   <message>
        //     <login>{Login}</login>
        //     <pwd>{ApiKey}</pwd>
        //     <id>{transactionId}</id>
        //     <sender>{Sender}</sender>
        //     <text>{message}</text>
        //     <phones><phone>{phoneNumber}</phone></phones>
        //   </message>
        // The gateway replies with an XML <response> carrying a <status> code; non-zero
        // means rejection. Swap the endpoint/shape here if the contract changes.
        var transactionId = Guid.NewGuid().ToString("N");

        var xml = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<message>")
            .Append("<login>").Append(SecurityElementEscape(_options.Login)).Append("</login>")
            .Append("<pwd>").Append(SecurityElementEscape(_options.ApiKey)).Append("</pwd>")
            .Append("<id>").Append(transactionId).Append("</id>")
            .Append("<sender>").Append(SecurityElementEscape(_options.Sender)).Append("</sender>")
            .Append("<text>").Append(SecurityElementEscape(message)).Append("</text>")
            .Append("<phones><phone>").Append(SecurityElementEscape(phoneNumber)).Append("</phone></phones>")
            .Append("</message>")
            .ToString();

        var requestUri = new Uri(new Uri(EnsureTrailingSlash(_options.BaseUrl)), "message/");
        using var content = new StringContent(xml, Encoding.UTF8, "application/xml");
        using var response = await _http.PostAsync(requestUri, content, ct);

        // Do not log the message body (may contain an OTP) — only the outcome.
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Nikita Pro SMS send failed. TransactionId={TransactionId} Status={StatusCode}",
                transactionId, (int)response.StatusCode);
            throw new HttpRequestException(
                $"Nikita Pro SMS gateway returned HTTP {(int)response.StatusCode}.");
        }

        _logger.LogInformation(
            "Nikita Pro SMS dispatched. TransactionId={TransactionId}", transactionId);
    }

    // Minimal XML text escaping for body values.
    private static string SecurityElementEscape(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";
}
