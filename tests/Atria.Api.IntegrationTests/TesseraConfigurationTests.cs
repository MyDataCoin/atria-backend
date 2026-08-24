using System.ComponentModel.DataAnnotations;
using Atria.Infrastructure.Configuration;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// The verification policy decides whether an investor may be allowlisted at all, so a blank one is
/// not a missing convenience — it is a gate that silently never opens. Catching it at start turns a
/// mint that dies in a worker log into a process that says what is missing before it serves
/// anything.
/// </summary>
public sealed class TesseraConfigurationTests
{
    private static IReadOnlyList<string> Validate(TesseraOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, true);

        return results.Select(r => r.ErrorMessage ?? string.Empty).ToList();
    }

    [Fact]
    public void A_configured_policy_and_issuer_start_cleanly()
        => Validate(new TesseraOptions
        {
            PolicyId = "atria-investor-policy-v1",
            IssuerDid = "did:atria:issuer"
        }).Should().BeEmpty();

    [Fact]
    public void A_missing_policy_refuses_to_start()
        => Validate(new TesseraOptions { PolicyId = null!, IssuerDid = "did:atria:issuer" })
            .Should().ContainSingle().Which.Should().Contain(nameof(TesseraOptions.PolicyId));

    /// <summary>
    /// Blank, not just absent: a section present in configuration with an empty value reads as
    /// "configured" to a human scanning the file, and would otherwise pass straight through.
    /// </summary>
    [Fact]
    public void A_blank_policy_refuses_to_start()
        => Validate(new TesseraOptions { PolicyId = "   ", IssuerDid = "did:atria:issuer" })
            .Should().ContainSingle().Which.Should().Contain(nameof(TesseraOptions.PolicyId));

    [Fact]
    public void A_missing_issuer_did_refuses_to_start()
        => Validate(new TesseraOptions { PolicyId = "atria-investor-policy-v1", IssuerDid = null! })
            .Should().ContainSingle().Which.Should().Contain(nameof(TesseraOptions.IssuerDid));
}
