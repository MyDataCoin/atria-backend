using Atria.Application.Abstractions;
using Atria.Infrastructure.Compliance;
using Atria.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Atria.Application.Tests.Compliance;

/// <summary>
/// The network belongs to the issue, not to the application. These tests pin the behaviour that
/// makes a second issue on a second network a settings change rather than a rewrite.
/// </summary>
public sealed class ChainNetworkResolverTests
{
    private static ChainNetworkResolver NewResolver(params (string Tag, long ChainId)[] networks)
        => new(Options.Create(new BlockchainOptions
        {
            SignerUrl = "https://signer.test",
            Networks = networks.ToDictionary(
                n => n.Tag,
                n => new ChainNetworkOptions
                {
                    ChainId = n.ChainId,
                    RpcUrl = $"https://rpc.{n.Tag}.test",
                    IdentityRegistryAddress = "0x3838f73f9787f8b4f8a1e0173de7c7030a570806",
                    AllowlistAddress = "0x5f1586bDaCD7bbD6da2E8EB377C51A268afd40D2"
                })
        }));

    [Fact]
    public void A_configured_network_resolves_by_its_tag()
    {
        var resolver = NewResolver(("bsc-testnet", 97));

        var network = resolver.Resolve("bsc-testnet");

        network.Should().NotBeNull();
        network!.ChainId.Should().Be(97);
        network.RpcUrl.Should().Be("https://rpc.bsc-testnet.test");
    }

    [Fact]
    public void Tags_match_regardless_of_case()
    {
        var resolver = NewResolver(("bsc-testnet", 97));

        resolver.Resolve("BSC-Testnet").Should().NotBeNull();
    }

    /// <summary>
    /// An unknown or missing tag resolves to nothing so the caller fails loudly. Falling back to
    /// "the one configured network" is how an operation reaches the wrong chain.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ethereum-mainnet")]
    public void An_unknown_or_missing_tag_resolves_to_nothing(string? tag)
    {
        var resolver = NewResolver(("bsc-testnet", 97));

        resolver.Resolve(tag).Should().BeNull();
    }

    [Fact]
    public void Several_networks_coexist_and_each_keeps_its_own_settings()
    {
        var resolver = NewResolver(("bsc-testnet", 97), ("bsc-mainnet", 56));

        resolver.Resolve("bsc-testnet")!.ChainId.Should().Be(97);
        resolver.Resolve("bsc-mainnet")!.ChainId.Should().Be(56);
        resolver.All.Should().HaveCount(2);
    }

    [Fact]
    public void With_nothing_configured_every_tag_resolves_to_nothing()
    {
        var resolver = NewResolver();

        resolver.Resolve("bsc-testnet").Should().BeNull();
        resolver.All.Should().BeEmpty();
    }
}
