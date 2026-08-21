using Atria.Infrastructure.Configuration;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// The operational-key mode is asked for explicitly and only on test networks, which is exactly when
/// a half-filled configuration is likeliest. Catching it at start turns a mint that dies in a worker
/// log into a process that says what is missing before it serves anything.
/// </summary>
public sealed class TokenSigningConfigurationTests
{
    // A throwaway key, valid only as a well-formed 32-byte value.
    private const string Key = "0x4c0883a69102937d6231471b5dbb6204fe5129617082792ae468d01a3f362318";

    private static ValidateOptionsOutcome Validate(TokenSigningOptions options)
    {
        var result = new TokenSigningOptionsValidator().Validate(null, options);
        return new ValidateOptionsOutcome(result.Succeeded, result.Failures ?? Array.Empty<string>());
    }

    private sealed record ValidateOptionsOutcome(bool Succeeded, IEnumerable<string> Failures);

    /// <summary>The production posture holds no keys at all, so there is nothing here to require.</summary>
    [Fact]
    public void The_custody_mode_needs_no_keys()
        => Validate(new TokenSigningOptions { Mode = TokenSigningMode.Custody })
            .Succeeded.Should().BeTrue();

    [Fact]
    public void The_operational_mode_without_keys_refuses_to_start()
    {
        var outcome = Validate(new TokenSigningOptions { Mode = TokenSigningMode.OperationalKey });

        outcome.Succeeded.Should().BeFalse();
        outcome.Failures.Should().HaveCount(2);
        outcome.Failures.Should().Contain(f => f.Contains("MinterPrivateKey"))
            .And.Contain(f => f.Contains("OraclePrivateKey"));
    }

    /// <summary>
    /// The oracle key is checked as strictly as the minter key: an issue whose collateral cannot be
    /// attested is missing the part a regulator reads, and today that failure waits until the first
    /// appraisal is filed.
    /// </summary>
    [Fact]
    public void The_operational_mode_requires_the_oracle_key_too()
    {
        var outcome = Validate(new TokenSigningOptions
        {
            Mode = TokenSigningMode.OperationalKey,
            MinterPrivateKey = Key
        });

        outcome.Succeeded.Should().BeFalse();
        outcome.Failures.Should().ContainSingle().Which.Should().Contain("OraclePrivateKey");
    }

    /// <summary>A malformed key reads as a working configuration until the first transaction.</summary>
    [Fact]
    public void A_key_that_is_not_a_key_is_refused()
    {
        var outcome = Validate(new TokenSigningOptions
        {
            Mode = TokenSigningMode.OperationalKey,
            MinterPrivateKey = "0xdeadbeef",
            OraclePrivateKey = Key
        });

        outcome.Succeeded.Should().BeFalse();
        outcome.Failures.Should().ContainSingle().Which.Should().Contain("not a valid private key");
    }

    [Fact]
    public void A_complete_operational_configuration_starts()
        => Validate(new TokenSigningOptions
        {
            Mode = TokenSigningMode.OperationalKey,
            MinterPrivateKey = Key,
            OraclePrivateKey = Key
        }).Succeeded.Should().BeTrue();
}
