using Atria.Api.Startup;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// A service key and an investor wallet must never be the same address. One address doing both means
/// the platform holds a client's key — the opposite of what the custody design promises — and makes
/// every on-chain action ambiguous afterwards: nobody can tell the holder from the operator.
/// </summary>
public sealed class SigningKeySeparationTests
{
    private const string AgentSetting = "Blockchain:Anchor:AgentPrivateKey";
    private const string MinterSetting = "Blockchain:TokenSigning:MinterPrivateKey";

    private const string ServiceAgent = "0x23E3C895cC4f77B85443feE0042Dc105BeB00B93";
    private const string InvestorWallet = "0x11135c2C5683f1D19cC740B462B77e1C23Fee92C";

    private static IReadOnlyList<string> Check(
        Dictionary<string, string> signing, params string[] wallets)
        => SigningKeySeparationExtensions.FindCollisions(signing, wallets);

    [Fact]
    public void Dedicated_service_keys_pass()
        => Check(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ServiceAgent] = AgentSetting,
            },
            InvestorWallet).Should().BeEmpty();

    [Fact]
    public void An_investor_wallet_used_as_a_service_key_is_refused()
    {
        var collisions = Check(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [InvestorWallet] = AgentSetting,
            },
            InvestorWallet);

        collisions.Should().ContainSingle()
            .Which.Should().Contain(AgentSetting).And.Contain(InvestorWallet);
    }

    /// <summary>
    /// Checksum casing is presentational: the same address written two ways is one address, and a
    /// case-sensitive comparison would wave through precisely the collision being looked for.
    /// </summary>
    [Fact]
    public void Casing_does_not_hide_a_collision()
        => Check(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [InvestorWallet.ToUpperInvariant()] = AgentSetting,
            },
            InvestorWallet.ToLowerInvariant()).Should().ContainSingle();

    /// <summary>Each offending setting is named, so one message is enough to fix the configuration.</summary>
    [Fact]
    public void Every_offending_setting_is_named()
    {
        var other = "0x1e33c38838E4aB8F3d01f003A63D956Ed3d0D506";

        var collisions = Check(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [InvestorWallet] = AgentSetting,
                [other] = MinterSetting,
            },
            InvestorWallet, other);

        collisions.Should().HaveCount(2);
        collisions.Should().Contain(c => c.Contains(AgentSetting))
            .And.Contain(c => c.Contains(MinterSetting));
    }

    [Fact]
    public void No_investors_yet_is_not_a_collision()
        => Check(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ServiceAgent] = AgentSetting,
        }).Should().BeEmpty();
}
