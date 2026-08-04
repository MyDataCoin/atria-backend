using System.Reflection;
using FluentAssertions;
using Nethereum.Util;

namespace Atria.Application.Tests.Compliance;

/// <summary>
/// Pins the hard-coded function selectors in <c>CustodyTokenGateway</c> to the signatures they claim
/// to encode.
/// </summary>
/// <remarks>
/// <para>
/// The custody path builds call data by hand — <c>FunctionCallEncoder.EncodeRequest("40c10f19", …)</c>
/// — because custody signs bytes it can check against the ABI rather than re-deriving our intent
/// from prose. The cost of that is two repositories agreeing on a four-byte constant with nothing
/// enforcing it: change <c>mint</c>'s signature in atria-contracts and this solution still compiles,
/// still passes its tests, and produces call data custody will happily sign for a function that no
/// longer exists. It reverts on chain, after gas, and <see cref="CustodyTokenGateway"/> reports
/// <c>Confirmed: false</c> either way, so the failure surfaces late and indirectly.
/// </para>
/// <para>
/// Recomputing the selector from the signature string is cheap and catches the whole class of drift.
/// The signature strings themselves are checked against the deployed ABI by the contracts repo's own
/// suite; what this guards is the C# side agreeing with them.
/// </para>
/// </remarks>
public sealed class ContractSelectorTests
{
    [Theory]
    // AtriaPropertyToken.mint(address to, uint256 amount)
    [InlineData("mint(address,uint256)", "40c10f19")]
    // AtriaPropertyToken.reportCollateral(bytes32 dataHash, uint256 valuation, uint64 valuedAt, string uri)
    [InlineData("reportCollateral(bytes32,uint256,uint64,string)", "d2b3d0db")]
    public void Selector_matches_the_signature_it_claims_to_encode(string signature, string expected)
    {
        var actual = Convert.ToHexString(
            new Sha3Keccack().CalculateHash(System.Text.Encoding.UTF8.GetBytes(signature))[..4]);

        actual.Should().BeEquivalentTo(
            expected,
            $"the gateway encodes '{signature}' as 0x{expected}; if the contract's signature changed, "
            + "the call data is now for a function that does not exist");
    }

    /// <summary>
    /// The constants are only worth pinning if they are still the ones the gateway uses, so this
    /// fails if someone adds a third hand-encoded call without extending the table above.
    /// </summary>
    [Fact]
    public void Every_hand_encoded_selector_in_the_gateway_is_covered()
    {
        var gateway = typeof(Atria.Infrastructure.Compliance.CustodyTokenGateway);
        var source = ReadSourceOf(gateway);

        var used = System.Text.RegularExpressions.Regex
            .Matches(source, @"sha3Signature:\s*""([0-9a-fA-F]{8})""")
            .Select(m => m.Groups[1].Value.ToUpperInvariant())
            .Distinct()
            .ToArray();

        var covered = new[] { "40C10F19", "D2B3D0DB" };

        used.Should().BeSubsetOf(
            covered,
            "every selector the gateway hard-codes needs a row in "
            + nameof(Selector_matches_the_signature_it_claims_to_encode));
    }

    /// <summary>Locates the gateway's source next to the test assembly's repository root.</summary>
    private static string ReadSourceOf(Type type)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Atria.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test runs from inside the repository");

        var path = Path.Combine(
            directory!.FullName, "src", "Atria.Infrastructure", "Compliance", type.Name + ".cs");

        File.Exists(path).Should().BeTrue($"expected the gateway source at {path}");
        return File.ReadAllText(path);
    }
}
