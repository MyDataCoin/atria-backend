using System.Diagnostics.CodeAnalysis;

namespace Atria.Infrastructure.Configuration;

/// <summary>Who signs writes to an issue's token contract.</summary>
public enum TokenSigningMode
{
    /// <summary>
    /// An external custody service signs. No key that can create or destroy shares lives in this
    /// process. This is the production posture and the default.
    /// </summary>
    Custody = 0,

    /// <summary>
    /// The platform signs with a key from configuration. For test networks, where there is no
    /// custody service to call and the shares are worth nothing.
    /// </summary>
    OperationalKey = 1
}

/// <summary>Signing settings for token operations (minting, and later burns and forced transfers).</summary>
public sealed class TokenSigningOptions
{
    public const string SectionName = "Blockchain:TokenSigning";

    /// <summary>
    /// Which signer is used. Defaults to custody: a key able to issue shares out of nothing should
    /// never end up in the application because someone forgot to set a flag.
    /// </summary>
    public TokenSigningMode Mode { get; init; } = TokenSigningMode.Custody;

    /// <summary>
    /// Minter key, used only when <see cref="Mode"/> is <see cref="TokenSigningMode.OperationalKey"/>.
    /// Must hold <c>MINTER_ROLE</c> and nothing else — never the admin multisig or the compliance key.
    /// Supply it through the guarded-secret path, never in a committed file.
    /// </summary>
    public string? MinterPrivateKey { get; init; }

    /// <summary>
    /// Oracle key, used only in the operational-key mode. Must hold <c>ORACLE_ROLE</c> and nothing
    /// else: it attests to what backs the issue and must not be able to touch the shares themselves.
    /// </summary>
    public string? OraclePrivateKey { get; init; }

    /// <summary>
    /// Compliance key, used only in the operational-key mode. Must hold <c>COMPLIANCE_ROLE</c>.
    /// </summary>
    /// <remarks>
    /// A THIRD key, not a spare minter. The contract puts burning behind a different role from
    /// minting on purpose: whoever can create shares must not be able to destroy someone's property.
    /// This is the key behind the 14-day right of withdrawal (§44), redemption, and withdrawal from
    /// circulation when an issue is declared invalid (§73).
    /// </remarks>
    public string? CompliancePrivateKey { get; init; }

    /// <summary>True when the operational-key path is selected but no key was supplied.</summary>
    [MemberNotNullWhen(false, nameof(MinterPrivateKey))]
    public bool IsMisconfigured
        => Mode == TokenSigningMode.OperationalKey && string.IsNullOrWhiteSpace(MinterPrivateKey);
}
