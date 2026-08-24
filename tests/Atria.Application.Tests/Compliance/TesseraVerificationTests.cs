using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Atria.Infrastructure.Compliance;
using Atria.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Atria.Application.Tests.Compliance;

/// <summary>
/// Verification decides whether an approved application ever reaches the chain, and until now the
/// service holding that decision had no test of its own: the handler above talks to a stubbed
/// interface, so the real rule was never exercised. That gap is how a verification which refused
/// EVERY investor survived — the caller passed an empty policy id, the service compared it against
/// the configured one, and no profile could ever satisfy it.
/// </summary>
public sealed class TesseraVerificationTests
{
    private static readonly Guid InvestorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Wallet = "0xaaaa000000000000000000000000000000000000";

    private readonly IComplianceRepository _profiles = Substitute.For<IComplianceRepository>();

    private TesseraComplianceService NewService(string policyId = "atria-investor-policy-v1")
        => new(
            _profiles,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IChainAnchor>(),
            Substitute.For<IChainNetworkResolver>(),
            Substitute.For<IBlockchainOperationQueue>(),
            Options.Create(new TesseraOptions { PolicyId = policyId, IssuerDid = "did:atria:issuer" }),
            NullLogger<TesseraComplianceService>.Instance);

    /// <summary>A profile as it stands after KYC approval: identifier issued, attestations recorded.</summary>
    private ComplianceProfile GivenApprovedProfile()
    {
        var profile = ComplianceProfile.Create(InvestorId, Wallet);
        profile.SetDid("did:atria:39e7c31c");
        profile.SetAttestations("""{"kyc_verified":true}""");
        _profiles.GetByInvestorAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(profile);

        return profile;
    }

    [Fact]
    public async Task An_approved_profile_verifies()
    {
        GivenApprovedProfile();

        var verified = await NewService().VerifyPresentationAsync(InvestorId, CancellationToken.None);

        verified.Should().BeTrue();
    }

    [Fact]
    public async Task A_revoked_profile_does_not_verify()
    {
        GivenApprovedProfile().Revoke("санкционный список");

        var verified = await NewService().VerifyPresentationAsync(InvestorId, CancellationToken.None);

        verified.Should().BeFalse();
    }

    [Fact]
    public async Task A_profile_without_a_did_does_not_verify()
    {
        var profile = ComplianceProfile.Create(InvestorId, Wallet);
        profile.SetAttestations("""{"kyc_verified":true}""");
        _profiles.GetByInvestorAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(profile);

        var verified = await NewService().VerifyPresentationAsync(InvestorId, CancellationToken.None);

        verified.Should().BeFalse();
    }

    [Fact]
    public async Task A_profile_without_attestations_does_not_verify()
    {
        var profile = ComplianceProfile.Create(InvestorId, Wallet);
        profile.SetDid("did:atria:39e7c31c");
        _profiles.GetByInvestorAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(profile);

        var verified = await NewService().VerifyPresentationAsync(InvestorId, CancellationToken.None);

        verified.Should().BeFalse();
    }

    [Fact]
    public async Task An_investor_with_no_profile_does_not_verify()
    {
        _profiles.GetByInvestorAsync(InvestorId, Arg.Any<CancellationToken>())
            .Returns((ComplianceProfile?)null);

        var verified = await NewService().VerifyPresentationAsync(InvestorId, CancellationToken.None);

        verified.Should().BeFalse();
    }

    /// <summary>
    /// An unconfigured policy refuses everyone rather than admitting everyone — the gate is missing,
    /// not open. Startup validation is meant to catch this first (see TesseraConfigurationTests);
    /// this is the second line.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_policy_does_not_verify()
    {
        GivenApprovedProfile();

        var verified = await NewService(policyId: "   ")
            .VerifyPresentationAsync(InvestorId, CancellationToken.None);

        verified.Should().BeFalse();
    }
}
