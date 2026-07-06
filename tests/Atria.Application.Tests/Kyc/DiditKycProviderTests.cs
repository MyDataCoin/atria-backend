using System.Net;
using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Kyc.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atria.Application.Tests.Kyc;

/// <summary>
/// Security + contract coverage for the Didit webhook path: HMAC-SHA256 signature
/// verification (X-Signature over the raw body) with replay protection, and decision
/// parsing of the Didit envelope (status + webhook_type).
/// </summary>
public sealed class DiditKycProviderTests
{
    private const string Secret = "didit-webhook-secret-test";

    private static DiditKycProvider CreateSut() =>
        new(new HttpClient(),
            Options.Create(new DiditOptions
            {
                ApiKey = "x",
                WebhookSecret = Secret,
                BaseUrl = "https://verification.didit.test",
                WebhookToleranceSeconds = 300
            }),
            NullLogger<DiditKycProvider>.Instance);

    // Overload wiring a stub transport so the decision-retrieval HTTP path can be exercised.
    private static DiditKycProvider CreateSut(HttpMessageHandler transport) =>
        new(new HttpClient(transport) { BaseAddress = new Uri("https://verification.didit.test") },
            Options.Create(new DiditOptions
            {
                ApiKey = "x",
                WebhookSecret = Secret,
                BaseUrl = "https://verification.didit.test",
                WebhookToleranceSeconds = 300
            }),
            NullLogger<DiditKycProvider>.Instance);

    private static WebhookPayload Payload(string body, string? signature, DateTimeOffset? ts) =>
        new(body, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), signature, ts, "1.2.3.4");

    private static string Hmac(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void VerifySignature_valid_hmac_and_fresh_timestamp_passes()
    {
        var sut = CreateSut();
        const string body = """{"event_id":"e1","session_id":"s1","status":"Approved","webhook_type":"status.updated"}""";

        sut.VerifySignature(Payload(body, Hmac(body), DateTimeOffset.UtcNow)).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_tampered_body_fails()
    {
        var sut = CreateSut();
        const string signed = """{"status":"Declined"}""";
        const string tampered = """{"status":"Approved"}""";

        // Signature computed over the signed body, but a different body is delivered.
        sut.VerifySignature(Payload(tampered, Hmac(signed), DateTimeOffset.UtcNow)).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_stale_timestamp_fails()
    {
        var sut = CreateSut();
        const string body = """{"status":"Approved"}""";

        // 10 minutes old > 300s tolerance -> rejected as a possible replay.
        sut.VerifySignature(Payload(body, Hmac(body), DateTimeOffset.UtcNow.AddMinutes(-10)))
            .Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_missing_signature_fails()
    {
        var sut = CreateSut();
        sut.VerifySignature(Payload("{}", signature: null, DateTimeOffset.UtcNow)).Should().BeFalse();
    }

    [Theory]
    [InlineData("Approved", KycDecision.Approved)]
    [InlineData("Declined", KycDecision.Declined)]
    [InlineData("In Review", KycDecision.Pending)]
    [InlineData("In Progress", KycDecision.Pending)]
    [InlineData("Expired", KycDecision.Pending)]
    public void ParseCallback_status_updated_maps_status(string status, KycDecision expected)
    {
        var sut = CreateSut();
        var body = $$"""{"event_id":"e9","session_id":"sess-9","status":"{{status}}","webhook_type":"status.updated"}""";

        var result = sut.ParseCallback(Payload(body, null, null));

        result.Decision.Should().Be(expected);
        result.ExternalSessionId.Should().Be("sess-9");
        result.EventId.Should().Be("e9");
    }

    [Fact]
    public void ParseCallback_non_status_event_is_pending_noop()
    {
        var sut = CreateSut();
        // A transaction event must never move KYC state.
        const string body = """{"event_id":"t1","status":"Approved","webhook_type":"transaction.created"}""";

        sut.ParseCallback(Payload(body, null, null)).Decision.Should().Be(KycDecision.Pending);
    }

    [Fact]
    public async Task RetrieveVerifiedIdentity_reads_verified_name_from_decision_and_hits_the_right_endpoint()
    {
        // Didit v3 user-KYC decision: verified ID fields live in the id_verifications[] array.
        const string json = """
            {"session_kind":"user","id_verifications":[
              {"node_id":"f1","first_name":"Carmen","last_name":"Española Española",
               "full_name":"Carmen Española Española","status":"Approved"}]}
            """;
        var transport = new StubTransport(HttpStatusCode.OK, json);
        var sut = CreateSut(transport);

        var identity = await sut.RetrieveVerifiedIdentityAsync("sess-1", CancellationToken.None);

        identity.Should().NotBeNull();
        identity!.FirstName.Should().Be("Carmen");
        identity.LastName.Should().Be("Española Española");
        identity.FullName.Should().Be("Carmen Española Española");
        // Correct endpoint (GET /v3/session/{id}/decision/) and x-api-key auth.
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.AbsolutePath.Should().Be("/v3/session/sess-1/decision/");
        transport.LastRequest.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("x");
    }

    [Fact]
    public async Task RetrieveVerifiedIdentity_supports_nested_object_shape()
    {
        // Alternative/older shape: a single id_verification object instead of the array.
        const string json = """{"id_verification":{"first_name":"Aibek","last_name":"Uulu"}}""";
        var sut = CreateSut(new StubTransport(HttpStatusCode.OK, json));

        var identity = await sut.RetrieveVerifiedIdentityAsync("sess-2", CancellationToken.None);

        identity.Should().NotBeNull();
        identity!.FirstName.Should().Be("Aibek");
        identity.LastName.Should().Be("Uulu");
    }

    [Fact]
    public async Task RetrieveVerifiedIdentity_returns_null_on_non_success_status()
    {
        // A failed retrieval must be a null (best-effort) result, never an exception.
        var sut = CreateSut(new StubTransport(HttpStatusCode.NotFound, """{"detail":"not found"}"""));

        (await sut.RetrieveVerifiedIdentityAsync("missing", CancellationToken.None)).Should().BeNull();
    }

    /// <summary>Canned-response transport that also captures the last outbound request.</summary>
    private sealed class StubTransport : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public HttpRequestMessage? LastRequest { get; private set; }

        public StubTransport(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
