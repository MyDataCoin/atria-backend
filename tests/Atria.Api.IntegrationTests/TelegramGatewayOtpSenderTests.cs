using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Atria.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atria.Api.IntegrationTests;

public sealed class TelegramGatewayOtpSenderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private static TelegramGatewayOtpSender Sut(StubHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new TelegramGatewayOptions { ApiToken = "test-token" }),
        NullLogger<TelegramGatewayOtpSender>.Instance);

    [Fact]
    public async Task Sends_the_code_with_bearer_token_phone_and_ttl()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"ok":true,"result":{"request_id":"r1"}}""");

        await Sut(handler).SendCodeAsync("+996700123456", "482913", 5, CancellationToken.None);

        handler.Request!.RequestUri!.ToString().Should().Be("https://gatewayapi.telegram.org/sendVerificationMessage");
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Request.Headers.Authorization.Parameter.Should().Be("test-token");

        using var doc = JsonDocument.Parse(handler.RequestBody!);
        doc.RootElement.GetProperty("phone_number").GetString().Should().Be("+996700123456");
        doc.RootElement.GetProperty("code").GetString().Should().Be("482913");
        doc.RootElement.GetProperty("ttl").GetInt32().Should().Be(300);
    }

    [Fact]
    public async Task A_refusal_becomes_a_gateway_error_the_otp_flow_reports_as_502()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"ok":false,"error":"BALANCE_NOT_ENOUGH"}""");

        var send = () => Sut(handler).SendCodeAsync("+996700123456", "482913", 5, CancellationToken.None);

        (await send.Should().ThrowAsync<SmsGatewayException>()).Which.Message.Should().Contain("BALANCE_NOT_ENOUGH");
    }
}
