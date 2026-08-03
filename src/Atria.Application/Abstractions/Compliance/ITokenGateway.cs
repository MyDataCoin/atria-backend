namespace Atria.Application.Abstractions;

/// <summary>Outcome of a token write.</summary>
/// <param name="TransactionRef">Chain-native transaction reference.</param>
/// <param name="Confirmed">Whether the write was observed on chain, not merely submitted.</param>
public sealed record TokenWriteResult(string TransactionRef, bool Confirmed);

/// <summary>
/// Writes to an issue's own token contract: issuing shares to an investor, and the compliance
/// powers that withdraw them again.
/// </summary>
/// <remarks>
/// Separate from <see cref="IAllowlistGateway"/> because the two are signed by different keys on
/// purpose. The allowlist is maintained by a scoped service key; the token is the issue itself, and
/// whoever can call these functions can create or destroy someone's property.
///
/// Order matters and is enforced by the contract, not by hope: an address must be on the allowlist
/// before anything can be minted to it, and a mint to an unlisted address reverts.
/// </remarks>
public interface ITokenGateway
{
    /// <summary>Issues <paramref name="amount"/> whole shares to an investor's address.</summary>
    Task<TokenWriteResult> MintAsync(
        string chainTag, string tokenContractAddress, string toAddress, long amount, CancellationToken ct);

    /// <summary>
    /// Delivers verified collateral data into the contract (draft Decree, §16): the hash of the
    /// document package, the appraised value and when it was appraised.
    /// </summary>
    /// <remarks>
    /// Signed by the oracle role, never by the minter. The contract does not check the data — it
    /// records what the oracle attested — so the value of the whole mechanism rests on that key being
    /// separate from the one that can issue shares.
    /// </remarks>
    Task<TokenWriteResult> ReportCollateralAsync(
        string chainTag, string tokenContractAddress, byte[] dataHash, decimal valuation,
        DateTime valuedAtUtc, string uri, CancellationToken ct);
}
