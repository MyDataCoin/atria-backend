using Atria.Application.Abstractions;
using Atria.Application.Kyc.Commands;
using Atria.Domain.Kyc;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atria.Application.Tests.Kyc;

/// <summary>
/// Covers <see cref="SyncKycFromProviderCommandHandler"/> — the way out of a profile stuck in
/// <c>UnderReview</c> because the provider's webhook never arrived. The handler asks the provider
/// what it decided and applies that, including the verified name; a provider that cannot be asked
/// must not be reported as "still under review", and a profile already decided is left alone.
/// </summary>
public sealed class SyncKycFromProviderCommandHandlerTests
{
    private const string SessionId = "sess-1";

    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IKycProviderStrategy _provider = Substitute.For<IKycProviderStrategy>();

    public SyncKycFromProviderCommandHandlerTests()
        => _provider.ProviderType.Returns(KycProviderType.Didit);

    private SyncKycFromProviderCommandHandler CreateSut() =>
        new(_kyc, new[] { _provider }, _uow, NullLogger<SyncKycFromProviderCommandHandler>.Instance);

    private KycProfile SubmittedProfile()
    {
        var profile = KycProfile.Create(Guid.NewGuid());
        profile.Submit(KycProviderType.Didit, SessionId, null, null, null, null, null);
        _kyc.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(profile);
        return profile;
    }

    [Fact]
    public async Task Applies_an_approval_the_webhook_never_delivered()
    {
        var profile = SubmittedProfile();
        _provider.RetrieveDecisionAsync(SessionId, Arg.Any<CancellationToken>())
            .Returns(new KycProviderDecision(
                KycDecision.Approved, "Approved", null,
                new KycVerifiedIdentity("Carmen", "Española", null)));

        var result = await CreateSut().Handle(new SyncKycFromProviderCommand(profile.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        profile.Status.Should().Be(KycStatus.Approved);
        // The name is the whole reason a pulled approval is as good as a delivered one.
        profile.FullName.Should().Be("Carmen Española");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Applies_a_decline_with_the_providers_reason()
    {
        var profile = SubmittedProfile();
        _provider.RetrieveDecisionAsync(SessionId, Arg.Any<CancellationToken>())
            .Returns(new KycProviderDecision(KycDecision.Declined, "Declined", "Document expired", null));

        var result = await CreateSut().Handle(new SyncKycFromProviderCommand(profile.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        profile.Status.Should().Be(KycStatus.Rejected);
        profile.RejectionReason.Should().Be("Document expired");
    }

    [Fact]
    public async Task Leaves_the_profile_alone_while_the_provider_has_not_decided()
    {
        var profile = SubmittedProfile();
        _provider.RetrieveDecisionAsync(SessionId, Arg.Any<CancellationToken>())
            .Returns(new KycProviderDecision(KycDecision.Pending, "In Review", null, null));

        var result = await CreateSut().Handle(new SyncKycFromProviderCommand(profile.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(KycStatus.UnderReview);
        profile.Status.Should().Be(KycStatus.UnderReview);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fails_loudly_when_the_provider_cannot_be_asked()
    {
        var profile = SubmittedProfile();
        _provider.RetrieveDecisionAsync(SessionId, Arg.Any<CancellationToken>())
            .Returns((KycProviderDecision?)null);

        var result = await CreateSut().Handle(new SyncKycFromProviderCommand(profile.Id), CancellationToken.None);

        // An unanswered question must never be reported as "still under review" — that would send an
        // operator away believing the provider had said something.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Kyc.ProviderUnreachable");
        profile.Status.Should().Be(KycStatus.UnderReview);
    }

    [Fact]
    public async Task Does_not_re_decide_a_profile_that_is_already_settled()
    {
        var profile = SubmittedProfile();
        profile.Approve();

        var result = await CreateSut().Handle(new SyncKycFromProviderCommand(profile.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(KycStatus.Approved);
        await _provider.DidNotReceive().RetrieveDecisionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
