using System.Net;
using System.Text;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atria.Application.Tests.Notifications;

/// <summary>
/// Contract coverage for the Nikita Pro (smspro.nikita.kg) SMS adapter, guarding the
/// fixes that made OTP delivery actually work:
/// (1) the request is POSTed to the documented <c>/api/message</c> endpoint as XML carrying
///     login/pwd/sender/text and the recipient as bare international digits (no leading '+');
/// (2) the gateway's XML <c>&lt;status&gt;</c> is honoured — only status 0 is success, any
///     non-zero status (or an unparseable / HTTP-error body) raises, instead of the old
///     behaviour that treated HTTP 200 alone as success.
/// </summary>
public sealed class NikitaProSmsAdapterTests
{
    private static readonly NikitaProOptions GatewayOptions = new()
    {
        Login = "sagynbaev",
        Sender = "MYDATACOIN",
        ApiKey = "secret-pwd",
        BaseUrl = "https://smspro.nikita.test/api/"
    };

    private static NikitaProSmsAdapter CreateSut(CapturingHandler handler) =>
        new(new HttpClient(handler),
            Options.Create(GatewayOptions),
            NullLogger<NikitaProSmsAdapter>.Instance);

    private static string AcceptedResponse() =>
        """<?xml version="1.0" encoding="UTF-8"?><response><id>x</id><status>0</status><phones>1</phones><smscnt>1</smscnt><message></message></response>""";

    [Fact]
    public async Task SendAsync_status_zero_posts_xml_to_message_endpoint_with_digits_only_phone()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, AcceptedResponse());
        var sut = CreateSut(handler);

        await sut.SendAsync("+996700123456", "Your Atria verification code is 123456.", CancellationToken.None);

        handler.RequestUri.Should().Be("https://smspro.nikita.test/api/message");
        handler.Method.Should().Be(HttpMethod.Post);
        handler.Body.Should().Contain("<login>sagynbaev</login>");
        handler.Body.Should().Contain("<pwd>secret-pwd</pwd>");
        handler.Body.Should().Contain("<sender>MYDATACOIN</sender>");
        // Recipient must be bare international digits — the gateway rejects a leading '+'.
        handler.Body.Should().Contain("<phone>996700123456</phone>");
        handler.Body.Should().NotContain("+996");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(15)]
    public async Task SendAsync_nonzero_gateway_status_throws(int status)
    {
        var body = $"<response><id>x</id><status>{status}</status><message>rejected</message></response>";
        var sut = CreateSut(new CapturingHandler(HttpStatusCode.OK, body));

        // HTTP 200 but a non-zero gateway status must NOT be treated as a delivered SMS.
        await sut.Invoking(s => s.SendAsync("+996700123456", "msg", CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendAsync_http_error_throws()
    {
        var sut = CreateSut(new CapturingHandler(HttpStatusCode.InternalServerError, "boom"));

        await sut.Invoking(s => s.SendAsync("+996700123456", "msg", CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendAsync_unparseable_body_throws()
    {
        // HTTP 200 with a non-XML body: surfaced as a failure rather than a false success.
        var sut = CreateSut(new CapturingHandler(HttpStatusCode.OK, "not xml at all"));

        await sut.Invoking(s => s.SendAsync("+996700123456", "msg", CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    /// <summary>Records the outgoing request and replays a canned response.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;

        public CapturingHandler(HttpStatusCode status, string responseBody)
        {
            _status = status;
            _responseBody = responseBody;
        }

        public string? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri;
            Method = request.Method;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/xml")
            };
        }
    }
}