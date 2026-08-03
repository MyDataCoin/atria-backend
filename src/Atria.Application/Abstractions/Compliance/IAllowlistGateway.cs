namespace Atria.Application.Abstractions;

/// <summary>
/// Reflects an already-made compliance decision on the transfer-restriction contract: an address is
/// allowed once its holder's verification passes, and dropped when it is revoked.
/// </summary>
/// <remarks>
/// The gateway does not decide whether anyone is eligible — that happens off-chain — it only writes
/// the decision. The chain tag is part of every call because the allowlist is deployed per network:
/// an investor verified once may hold shares in issues on different networks, and each of those
/// contracts has to be told separately.
/// </remarks>
public interface IAllowlistGateway
{
    /// <summary>Allows an address on the given network's restriction contract. Idempotent on chain.</summary>
    Task AddAsync(string chainTag, string address, CancellationToken ct);

    /// <summary>Disallows an address. Idempotent on chain.</summary>
    Task RemoveAsync(string chainTag, string address, CancellationToken ct);
}
