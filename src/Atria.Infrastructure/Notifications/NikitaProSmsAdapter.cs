// Added Secret Login and Pass (вне репо)
using System.Text;
using System.Xml.Linq;
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
        // Nikita Pro's "smspro" XML API (https://smspro.nikita.kg/api/message): an SSL POST
        // of the following body (id is an arbitrary unique request id):
        //   <?xml version="1.0" encoding="UTF-8"?>
        //   <message>
        //     <login>{Login}</login>
        //     <pwd>{ApiKey}</pwd>
        //     <id>{transactionId}</id>
        //     <sender>{Sender}</sender>
        //     <text>{message}</text>
        //     <phones><phone>{phoneNumber}</phone></phones>
        //   </message>
        // The gateway replies HTTP 200 with an XML <response><status>N</status>...</response>;
        // status 0 means accepted, any non-zero value means the request was rejected (e.g.
        // bad credentials, unknown sender, no balance). HTTP success alone is NOT enough.
        var transactionId = Guid.NewGuid().ToString("N");

        // The gateway expects bare international digits (e.g. 996700123456) — strip the
        // leading '+' and any separators that may have survived normalization.
        var phoneDigits = new string(phoneNumber.Where(char.IsDigit).ToArray());

        var xml = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .Append("<message>")
            .Append("<login>").Append(SecurityElementEscape(_options.Login)).Append("</login>")
            .Append("<pwd>").Append(SecurityElementEscape(_options.ApiKey)).Append("</pwd>")
            .Append("<id>").Append(transactionId).Append("</id>")
            .Append("<sender>").Append(SecurityElementEscape(_options.Sender)).Append("</sender>")
            .Append("<text>").Append(SecurityElementEscape(message)).Append("</text>")
            .Append("<phones><phone>").Append(SecurityElementEscape(phoneDigits)).Append("</phone></phones>")
            .Append("</message>")
            .ToString();

        var requestUri = new Uri(new Uri(EnsureTrailingSlash(_options.BaseUrl)), "message");
        using var content = new StringContent(xml, Encoding.UTF8, "application/xml");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(requestUri, content, ct);
        }
        catch (HttpRequestException ex)
        {
            // Transport failure (DNS, TLS, connection refused, etc.) — never surface as a generic 500.
            _logger.LogError(
                ex, "Nikita Pro SMS transport failure. TransactionId={TransactionId}", transactionId);
            throw new SmsGatewayException(
                gatewayStatus: null,
                $"Nikita Pro SMS gateway is unreachable (transaction {transactionId}).", ex);
        }

        using (response)
        {
            // Do not log the message body (may contain an OTP) — only the outcome.
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Nikita Pro SMS send failed (HTTP). TransactionId={TransactionId} Status={StatusCode}",
                    transactionId, (int)response.StatusCode);
                throw new SmsGatewayException(
                    gatewayStatus: null,
                    $"Nikita Pro SMS gateway returned HTTP {(int)response.StatusCode}.");
            }

            // Parse the XML <response> and treat any non-zero <status> as a failure.
            var body = await response.Content.ReadAsStringAsync(ct);
            var (status, gatewayMessage) = ParseResponse(body);
            if (status != 0)
            {
                _logger.LogError(
                    "Nikita Pro SMS rejected. TransactionId={TransactionId} GatewayStatus={Status} GatewayMessage={Message}",
                    transactionId, status?.ToString() ?? "<unparsed>", gatewayMessage);
                throw new SmsGatewayException(
                    status,
                    $"Nikita Pro SMS gateway rejected the message (status={status?.ToString() ?? "unparsed"}).");
            }
        }

        _logger.LogInformation(
            "Nikita Pro SMS dispatched. TransactionId={TransactionId}", transactionId);
    }

    /// <summary>
    /// Parses the gateway's <c>&lt;response&gt;&lt;status&gt;N&lt;/status&gt;&lt;message&gt;…&lt;/message&gt;&lt;/response&gt;</c>.
    /// Returns the status code (null if it could not be parsed) and the optional gateway message.
    /// </summary>
    /// <remarks>
    /// The live gateway wraps the response in a default namespace
    /// (<c>xmlns="http://Giper.mobi/schema/Message"</c>), so elements are matched by local name
    /// rather than by a namespace-qualified <see cref="XName"/> — otherwise a successful
    /// <c>status=0</c> would be read as <c>null</c> and a delivered SMS reported as a failure.
    /// </remarks>
    private static (int? Status, string? Message) ParseResponse(string body)
    {
        try
        {
            var root = XDocument.Parse(body).Root;
            var statusText = ElementByLocalName(root, "status")?.Value;
            var message = ElementByLocalName(root, "message")?.Value;
            return int.TryParse(statusText, out var status) ? (status, message) : (null, message);
        }
        catch (System.Xml.XmlException)
        {
            // Unparseable body: surface as a failure (status null) rather than a false success.
            return (null, null);
        }
    }

    /// <summary>Finds a direct child element by local name, ignoring any XML namespace.</summary>
    private static XElement? ElementByLocalName(XElement? root, string localName) =>
        root?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

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
