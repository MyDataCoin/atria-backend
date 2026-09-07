using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Trading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atria.Application.Tests.Trading;

/// <summary>
/// Everything the register learns about prices and counterparties enters through this check. If it
/// can be fooled, anyone can file trades against our issues — so the tests are about what must be
/// refused, not only what must pass.
/// </summary>
public sealed class HmacExchangeSignatureVerifierTests
{
    private const string Secret = "a-properly-long-shared-secret-value";
    private const string Body = """{"event_id":"EV-1","event_type":"trade_settled"}""";

    private static HmacExchangeSignatureVerifier NewVerifier(
        string? secret = Secret, string? previous = null, int tolerance = 300)
        => new(
            Options.Create(new ExchangeOptions
            {
                WebhookSecret = secret,
                PreviousWebhookSecret = previous,
                SignatureToleranceSeconds = tolerance
            }),
            NullLogger<HmacExchangeSignatureVerifier>.Instance);

    private static string Sign(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NowStamp()
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    [Fact]
    public void AGenuinelySignedEventIsAccepted()
    {
        var ts = NowStamp();

        var verdict = NewVerifier().Verify(Body, Sign(Secret, ts, Body), ts);

        verdict.Should().Be(SignatureVerdict.Valid);
    }

    [Fact]
    public void ThePrefixedFormIsAcceptedToo()
    {
        var ts = NowStamp();

        var verdict = NewVerifier().Verify(Body, $"sha256={Sign(Secret, ts, Body)}", ts);

        verdict.Should().Be(SignatureVerdict.Valid);
    }

    [Fact]
    public void ABodyAlteredAfterSigningIsRefused()
    {
        var ts = NowStamp();
        var signature = Sign(Secret, ts, Body);

        var tampered = Body.Replace("EV-1", "EV-2");
        var verdict = NewVerifier().Verify(tampered, signature, ts);

        verdict.Should().Be(SignatureVerdict.Invalid);
    }

    [Fact]
    public void ACapturedRequestReplayedLaterIsRefused()
    {
        // The timestamp is inside the signed string precisely so this fails. Signing the body alone
        // would leave a captured request valid for the lifetime of the key.
        var old = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds().ToString();

        var verdict = NewVerifier().Verify(Body, Sign(Secret, old, Body), old);

        verdict.Should().Be(SignatureVerdict.Stale);
    }

    [Fact]
    public void AMovedTimestampInvalidatesTheSignature()
    {
        // Re-stamping a captured request to defeat the freshness window changes the signed string,
        // so the signature no longer verifies.
        var old = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds().ToString();
        var signature = Sign(Secret, old, Body);

        var verdict = NewVerifier().Verify(Body, signature, NowStamp());

        verdict.Should().Be(SignatureVerdict.Invalid);
    }

    [Fact]
    public void BothKeysAreAcceptedDuringARotation()
    {
        // Otherwise rotating a key means either an outage or a window with nothing verified.
        const string oldSecret = "the-previous-shared-secret-value";
        var ts = NowStamp();

        var verdict = NewVerifier(secret: Secret, previous: oldSecret)
            .Verify(Body, Sign(oldSecret, ts, Body), ts);

        verdict.Should().Be(SignatureVerdict.Valid);
    }

    [Fact]
    public void ARetiredKeyStopsWorkingOnceItIsRemoved()
    {
        const string oldSecret = "the-previous-shared-secret-value";
        var ts = NowStamp();

        var verdict = NewVerifier(secret: Secret, previous: null)
            .Verify(Body, Sign(oldSecret, ts, Body), ts);

        verdict.Should().Be(SignatureVerdict.Invalid);
    }

    [Fact]
    public void NoSecretConfiguredMeansNothingIsAccepted()
    {
        var ts = NowStamp();

        var verdict = NewVerifier(secret: null)
            .Verify(Body, Sign(Secret, ts, Body), ts);

        // Refusing everything is correct while no operator is connected: an event we cannot
        // authenticate is worse than one that never arrived.
        verdict.Should().Be(SignatureVerdict.NotConfigured);
    }

    [Theory]
    [InlineData("CHANGE_ME")]
    [InlineData("changeme")]
    [InlineData("secret")]
    [InlineData("short")]
    public void APlaceholderOrTooShortSecretCountsAsNoSecret(string weak)
    {
        // The platform has already shipped a committed CHANGE_ME default once, which passed an
        // emptiness check and then produced valid signatures under a secret published in the repo.
        var ts = NowStamp();

        var verdict = NewVerifier(secret: weak).Verify(Body, Sign(weak, ts, Body), ts);

        verdict.Should().Be(SignatureVerdict.NotConfigured);
    }

    [Fact]
    public void AnEventWithNoSignatureAtAllIsRefused()
    {
        NewVerifier().Verify(Body, signature: null, NowStamp())
            .Should().Be(SignatureVerdict.Missing);
    }

    [Fact]
    public void AnEventWithNoTimestampIsRefused()
    {
        NewVerifier().Verify(Body, Sign(Secret, NowStamp(), Body), timestamp: null)
            .Should().Be(SignatureVerdict.Missing);
    }
}
