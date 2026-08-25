using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Refuses to start when the operational-key path is selected but cannot actually sign.
/// </summary>
/// <remarks>
/// The alternative is what the code did before: a process that boots, serves, accepts an investment,
/// queues the mint — and only then discovers it has no key. By that point the failure is buried in a
/// worker log and the operator is looking for it in the wrong place. Nothing here applies to the
/// custody path: whether the external signer answers is a question about the network, not about this
/// configuration, and a signer that is briefly unreachable is no reason to refuse to serve.
/// </remarks>
public sealed partial class TokenSigningOptionsValidator : IValidateOptions<TokenSigningOptions>
{
    // 32 bytes of hex, with or without the 0x prefix. Nethereum accepts shorter values and pads
    // them, which turns a truncated key into a different, valid key belonging to nobody.
    [GeneratedRegex("^(0x)?[0-9a-fA-F]{64}$")]
    private static partial Regex PrivateKeyRegex();

    public ValidateOptionsResult Validate(string? name, TokenSigningOptions options)
    {
        if (options.Mode != TokenSigningMode.OperationalKey)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        Check(options.MinterPrivateKey, "MinterPrivateKey", "shares cannot be issued", failures);
        Check(options.OraclePrivateKey, "OraclePrivateKey", "collateral cannot be attested", failures);
        // NOT a start-up failure, deliberately. Minting and attesting are what the platform does to
        // serve users; burning is what it does to undo one withdrawal. Refusing to boot over the
        // third key took production down over a capability nothing was using that minute — the
        // outage was worse than the gap. EvmTokenGateway raises a clear error when a burn is
        // actually attempted without it.

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Check(string? key, string setting, string consequence, List<string> failures)
    {
        var path = $"{TokenSigningOptions.SectionName}:{setting}";

        if (string.IsNullOrWhiteSpace(key))
        {
            failures.Add(
                $"{path} is empty while Mode is OperationalKey, so {consequence}. Supply the key "
                + "through the environment (never appsettings.json), or set Mode back to Custody.");
            return;
        }

        // A key that is present but malformed fails at exactly the same late moment as a missing one,
        // and reads as a working configuration until then.
        if (!PrivateKeyRegex().IsMatch(key.Trim()))
            failures.Add($"{path} is not a valid private key (expected 32 bytes of hex, 0x-prefixed).");
    }
}
