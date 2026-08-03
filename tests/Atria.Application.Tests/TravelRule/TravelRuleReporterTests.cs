using Atria.Application.Abstractions;
using Atria.Application.TravelRule;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;
using Atria.Domain.TravelRule;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atria.Application.Tests.TravelRule;

/// <summary>
/// FATF R.16: information must travel with a transfer of value between service providers. What
/// matters is which transfers owe a disclosure, that the disclosure is assembled from verified
/// identity rather than invented, and that a transfer never silently escapes the rule.
/// </summary>
public sealed class TravelRuleReporterTests
{
    private readonly ITravelRuleRepository _messages = Substitute.For<ITravelRuleRepository>();
    private readonly IComplianceRepository _profiles = Substitute.For<IComplianceRepository>();
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly ITravelRuleSettings _settings = Substitute.For<ITravelRuleSettings>();

    private const string Ours = "0x1111111111111111111111111111111111111111";
    private const string Outside = "0x2222222222222222222222222222222222222222";
    private const string TxHash = "0xdeadbeef";

    private readonly Guid _investorId = Guid.NewGuid();

    public TravelRuleReporterTests()
    {
        _settings.ThresholdAmount.Returns(100_000m);
        _settings.OriginatingVaspName.Returns("ATRIA");
    }

    // 100 KGS per share, so 1 000 shares is 100 000 — exactly the threshold.
    private static Property Issue()
        => Property.Create("Tower One", null, null, 1_000_000m, 100m, 10_000, "KGS");

    private void GivenOurInvestorAt(string address, bool verified = true)
    {
        var profile = ComplianceProfile.Create(_investorId, address);
        _profiles.GetByWalletAsync(address, Arg.Any<CancellationToken>()).Returns(profile);

        if (!verified) return;

        var kyc = KycProfile.Create(_investorId);
        kyc.Submit(KycProviderType.Didit, "session-1", null, address, "Айгуль Сатыбалдиева", "ID2233445", "KG");
        _kyc.GetByUserIdAsync(_investorId, Arg.Any<CancellationToken>()).Returns(kyc);
    }

    private TravelRuleReporter NewReporter()
        => new(_messages, _profiles, _kyc, _settings, NullLogger<TravelRuleReporter>.Instance);

    private TravelRuleMessage? Queued()
        => _messages.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ITravelRuleRepository.AddAsync))
            .Select(c => (TravelRuleMessage)c.GetArguments()[0]!)
            .LastOrDefault();

    [Fact]
    public async Task A_transfer_out_to_another_provider_is_reported()
    {
        GivenOurInvestorAt(Ours);

        var message = await NewReporter().ReportTransferAsync(
            Issue(), Ours, Outside, 1_000, TxHash, CancellationToken.None);

        message.Should().NotBeNull();
        message!.Direction.Should().Be(TravelRuleDirection.Outgoing);
        message.OriginatorName.Should().Be("Айгуль Сатыбалдиева");
        message.OriginatorDocumentNumber.Should().Be("ID2233445");
        message.Amount.Should().Be(100_000m);
        message.Status.Should().Be(TravelRuleStatus.Pending);
        message.BeneficiaryName.Should().BeNull("we do not guess at who is on the other side");
    }

    [Fact]
    public async Task A_transfer_below_the_threshold_owes_nothing()
    {
        GivenOurInvestorAt(Ours);

        var message = await NewReporter().ReportTransferAsync(
            Issue(), Ours, Outside, 999, TxHash, CancellationToken.None);

        message.Should().BeNull();
        Queued().Should().BeNull();
    }

    /// <summary>
    /// Two holders we already know are not two service providers. Nothing travels between providers,
    /// so nothing is owed.
    /// </summary>
    [Fact]
    public async Task A_move_between_two_of_our_own_holders_owes_nothing()
    {
        GivenOurInvestorAt(Ours);
        var otherProfile = ComplianceProfile.Create(Guid.NewGuid(), Outside);
        _profiles.GetByWalletAsync(Outside, Arg.Any<CancellationToken>()).Returns(otherProfile);

        var message = await NewReporter().ReportTransferAsync(
            Issue(), Ours, Outside, 1_000, TxHash, CancellationToken.None);

        message.Should().BeNull();
    }

    /// <summary>A transfer between two outside addresses is not ours to report — and we have no verified identity for it.</summary>
    [Fact]
    public async Task A_transfer_between_two_outside_addresses_is_not_ours_to_report()
    {
        var message = await NewReporter().ReportTransferAsync(
            Issue(), Outside, Ours, 1_000, TxHash, CancellationToken.None);

        message.Should().BeNull();
    }

    /// <summary>
    /// Replaying a block must not produce a second disclosure. One transfer owes one.
    /// </summary>
    [Fact]
    public async Task Replaying_the_same_transfer_does_not_report_it_twice()
    {
        GivenOurInvestorAt(Ours);
        var already = TravelRuleMessage.Assemble(
            Guid.NewGuid(), _investorId, TravelRuleDirection.Outgoing, 1_000, 100_000m, "KGS",
            Ours, Outside, "Айгуль Сатыбалдиева", "ID2233445", "KG", null, null, TxHash);
        _messages.FindByTransferAsync(TxHash, Ours, Outside, Arg.Any<CancellationToken>())
            .Returns(already);

        var message = await NewReporter().ReportTransferAsync(
            Issue(), Ours, Outside, 1_000, TxHash, CancellationToken.None);

        message.Should().Be(already);
        await _messages.DidNotReceive().AddAsync(
            Arg.Any<TravelRuleMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A holder whose file has no verified name cannot be reported. The transfer has already
    /// happened on chain, so the reporter must not throw — the registry sync would be blocked and
    /// the register would end up wrong as well as the disclosure missing.
    /// </summary>
    [Fact]
    public async Task An_unverified_originator_produces_no_message_and_no_exception()
    {
        GivenOurInvestorAt(Ours, verified: false);

        var act = async () => await NewReporter().ReportTransferAsync(
            Issue(), Ours, Outside, 1_000, TxHash, CancellationToken.None);

        await act.Should().NotThrowAsync();
        Queued().Should().BeNull();
    }
}

/// <summary>
/// The payload that travels with the transfer. IVMS101 is the one part of the obligation that does
/// not depend on which counterparty network is chosen.
/// </summary>
public sealed class TravelRulePayloadTests
{
    private static TravelRuleMessage Message(string? document = "ID2233445", string? beneficiary = null)
        => TravelRuleMessage.Assemble(
            Guid.NewGuid(), Guid.NewGuid(), TravelRuleDirection.Outgoing, 1_000, 100_000m, "KGS",
            "0xaaa", "0xbbb", "Айгуль Сатыбалдиева", document, "KG", beneficiary, "Some Exchange",
            "0xdeadbeef");

    [Fact]
    public void The_originator_travels_with_the_transfer()
    {
        var json = TravelRulePayload.Build(Message(), "ATRIA", "LEI-123");

        json.Should().Contain("Айгуль Сатыбалдиева", "escaping it would leave the counterparty reading \\u sequences");
        json.Should().Contain("ID2233445").And.Contain("LEI-123").And.Contain("0xdeadbeef");
    }

    /// <summary>
    /// "Not provided" and "provided as blank" are different statements to a counterparty's checks,
    /// and only the first one is true here.
    /// </summary>
    [Fact]
    public void A_field_we_never_verified_is_absent_rather_than_empty()
    {
        var json = TravelRulePayload.Build(Message(document: null), "ATRIA", null);

        json.Should().Contain("\"nationalIdentification\":null");
        json.Should().NotContain("\"nationalIdentifier\":\"\"");
    }

    [Fact]
    public void The_beneficiary_person_is_absent_until_the_counterparty_names_them()
    {
        var json = TravelRulePayload.Build(Message(), "ATRIA", null);

        json.Should().Contain("\"beneficiaryPersons\":null");
        json.Should().Contain("0xbbb", "the receiving address is what we can actually see");
    }
}
