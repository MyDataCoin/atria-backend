using Atria.Application.Abstractions;
using Atria.Application.Kyc.Commands;
using Atria.Domain.Kyc;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Atria.Application.Tests.Kyc;

/// <summary>
/// Covers the verified-name enrichment on the Didit approval path in
/// <see cref="HandleKycCallbackCommandHandler"/>: on approval the handler pulls the verified
/// name from the provider's decision and writes it onto <c>KycProfile.FullName</c> (replacing the
/// self-reported one), while a missing/failed retrieval keeps the self-reported name and never
/// blocks approval. A decline does not query the provider for identity at all.
///
/// NSubstitute for the provider / repository / unit of work; a real in-memory
/// <see cref="IProcessedEventStore"/> double for the exactly-once guard.
/// </summary>
public sealed class HandleKycCallbackCommandHandlerTests
{
    private const string SessionId = "sess-1";

    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly InMemoryProcessedEventStore _processed = new();
    private readonly IKycProviderStrategy _provider = Substitute.For<IKycProviderStrategy>();

    public HandleKycCallbackCommandHandlerTests()
    {
        // Default: a verified Didit provider that approves the session. Individual tests override
        // ParseCallback / RetrieveVerifiedIdentityAsync as needed.
        _provider.ProviderType.Returns(KycProviderType.Didit);
        _provider.VerifySignature(Arg.Any<WebhookPayload>()).Returns(true);
        _provider.ParseCallback(Arg.Any<WebhookPayload>())
            .Returns(new KycCallbackResult(SessionId, KycDecision.Approved, null, "evt-1"));
    }

    private HandleKycCallbackCommandHandler CreateSut() =>
        new(new[] { _provider }, _kyc, _processed, _uow,
            NullLogger<HandleKycCallbackCommandHandler>.Instance);

    private static HandleKycCallbackCommand Command() =>
        new("Didit", new WebhookPayload("{}", new Dictionary<string, string>(), null, null, "1.2.3.4"));

    private KycProfile SubmittedProfile(string? selfReportedFullName)
    {
        var profile = KycProfile.Create(Guid.NewGuid());
        profile.Submit(KycProviderType.Didit, SessionId, null, null, selfReportedFullName, null, null);
        _kyc.GetBySessionIdAsync(SessionId, Arg.Any<CancellationToken>()).Returns(profile);
        return profile;
    }

    [Fact]
    public async Task Approved_writes_verified_full_name_onto_the_profile()
    {
        // Arrange — user self-reported "Typo Name"; the ID document says otherwise.
        var profile = SubmittedProfile("Typo Name");
        _provider.RetrieveVerifiedIdentityAsync(SessionId, Arg.Any<CancellationToken>())
            .Returns(new KycVerifiedIdentity("Carmen", "Española Española", "Carmen Española Española"));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(), CancellationToken.None);

        // Assert — verified full name persisted on the KYC record; profile approved and saved once.
        result.IsSuccess.Should().BeTrue();
        profile.FullName.Should().Be("Carmen Española Española");
        profile.Status.Should().Be(KycStatus.Approved);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approved_composes_full_name_from_split_parts_when_provider_returns_no_full_name()
    {
        // Arrange — provider returns only split first/last, no combined full name.
        var profile = SubmittedProfile(selfReportedFullName: null);
        _provider.RetrieveVerifiedIdentityAsync(SessionId, Arg.Any<CancellationToken>())
            .Returns(new KycVerifiedIdentity("Aibek", "Uulu", null));
        var sut = CreateSut();

        // Act
        await sut.Handle(Command(), CancellationToken.None);

        // Assert — first/last joined into the single FullName field.
        profile.FullName.Should().Be("Aibek Uulu");
        profile.Status.Should().Be(KycStatus.Approved);
    }

    [Fact]
    public async Task Approved_keeps_self_reported_name_when_no_verified_identity()
    {
        // Arrange — provider exposes no verified identity (e.g. feature disabled).
        var profile = SubmittedProfile("Jane Doe");
        _provider.RetrieveVerifiedIdentityAsync(SessionId, Arg.Any<CancellationToken>())
            .Returns((KycVerifiedIdentity?)null);
        var sut = CreateSut();

        // Act
        await sut.Handle(Command(), CancellationToken.None);

        // Assert — the self-reported name stands; approval still succeeds.
        profile.FullName.Should().Be("Jane Doe");
        profile.Status.Should().Be(KycStatus.Approved);
    }

    [Fact]
    public async Task Approved_keeps_self_reported_name_when_retrieval_throws()
    {
        // Arrange — a transport failure retrieving the decision must not break approval.
        var profile = SubmittedProfile("Jane Doe");
        _provider.RetrieveVerifiedIdentityAsync(SessionId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("didit down"));
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(), CancellationToken.None);

        // Assert — approval succeeds; self-reported name retained.
        result.IsSuccess.Should().BeTrue();
        profile.FullName.Should().Be("Jane Doe");
        profile.Status.Should().Be(KycStatus.Approved);
    }

    [Fact]
    public async Task Declined_does_not_query_verified_identity()
    {
        // Arrange — a decline moves state to Rejected and never fetches the identity.
        var profile = SubmittedProfile("Jane Doe");
        _provider.ParseCallback(Arg.Any<WebhookPayload>())
            .Returns(new KycCallbackResult(SessionId, KycDecision.Declined, "blurry document", "evt-2"));
        var sut = CreateSut();

        // Act
        await sut.Handle(Command(), CancellationToken.None);

        // Assert
        profile.Status.Should().Be(KycStatus.Rejected);
        await _provider.DidNotReceive()
            .RetrieveVerifiedIdentityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Real (non-mock) processed-event ledger; mirrors the existing handler tests.</summary>
    private sealed class InMemoryProcessedEventStore : IProcessedEventStore
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public Task<bool> IsProcessedAsync(string key, CancellationToken ct)
            => Task.FromResult(_keys.Contains(key));

        public Task MarkProcessedAsync(string key, CancellationToken ct)
        {
            _keys.Add(key);
            return Task.CompletedTask;
        }
    }
}
