using Atria.Application.Abstractions;
using Atria.Application.Kyc.Commands;
using Atria.Application.Kyc.Validators;
using Atria.Domain.Kyc;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atria.Application.Tests.Kyc;

/// <summary>
/// Regression cover for the KYC self-approval finding of the 2026-08-18 review.
///
/// <c>POST /api/v1/webhooks/kyc/{provider}</c> is anonymous and took the provider name straight
/// from the URL, so "manual" resolved to the internal back-office strategy — which has no vendor
/// and therefore no signature to check. Its session id is the caller's own KYC profile id, handed
/// back by <c>POST /kyc/submit</c>, so anyone could post their own id with <c>approved: true</c>
/// and walk through AML verification. These tests pin both locks: the dispatcher refuses internal
/// providers over the webhook, and a caller cannot pick the internal provider when starting a
/// session in the first place.
/// </summary>
public sealed class ManualKycWebhookHardeningTests
{
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IProcessedEventStore _processed = Substitute.For<IProcessedEventStore>();

    /// <summary>
    /// A stand-in for the internal provider that trusts everything, exactly as the real one did.
    /// If the dispatcher ever consults it again, these tests fail rather than the platform.
    /// </summary>
    private readonly IKycProviderStrategy _manual = Substitute.For<IKycProviderStrategy>();

    public ManualKycWebhookHardeningTests()
    {
        _manual.ProviderType.Returns(KycProviderType.Manual);
        _manual.VerifySignature(Arg.Any<WebhookPayload>()).Returns(true);
        _manual.ParseCallback(Arg.Any<WebhookPayload>())
            .Returns(new KycCallbackResult("sess-1", KycDecision.Approved, null, "evt-1"));
    }

    private HandleKycCallbackCommandHandler CreateSut() =>
        new(new[] { _manual }, _kyc, _processed, _uow,
            NullLogger<HandleKycCallbackCommandHandler>.Instance);

    [Theory]
    [InlineData("manual")]
    [InlineData("Manual")]
    [InlineData("MANUAL")]
    public async Task Webhook_refuses_the_internal_provider_whatever_the_casing(string provider)
    {
        var command = new HandleKycCallbackCommand(
            provider,
            new WebhookPayload(
                """{"sessionId":"sess-1","approved":true}""",
                new Dictionary<string, string>(), null, null, "1.2.3.4"));

        var result = await CreateSut().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Kyc.UnknownProvider");
    }

    [Fact]
    public async Task Webhook_never_looks_up_a_profile_for_the_internal_provider()
    {
        // The refusal has to land BEFORE the session lookup: a caller must not be able to learn
        // whether a session id exists, let alone move it to Approved.
        var command = new HandleKycCallbackCommand(
            "manual",
            new WebhookPayload(
                """{"sessionId":"sess-1","approved":true}""",
                new Dictionary<string, string>(), null, null, "1.2.3.4"));

        await CreateSut().Handle(command, CancellationToken.None);

        await _kyc.DidNotReceiveWithAnyArgs().GetBySessionIdAsync(default!, default);
        _manual.DidNotReceiveWithAnyArgs().VerifySignature(default!);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public void Submitting_kyc_cannot_select_the_internal_provider()
    {
        var validator = new SubmitKycCommandValidator();

        var result = validator.Validate(
            new SubmitKycCommand(KycProviderType.Manual, null, null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SubmitKycCommand.Provider));
    }

    [Fact]
    public void Submitting_kyc_with_a_real_external_provider_is_still_accepted()
    {
        var validator = new SubmitKycCommandValidator();

        var result = validator.Validate(
            new SubmitKycCommand(KycProviderType.Didit, null, null, null, null));

        result.IsValid.Should().BeTrue();
    }
}
