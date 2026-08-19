namespace Atria.Application.Abstractions;

/// <summary>How rotation of refresh tokens treats a token that was already rotated away.</summary>
/// <remarks>
/// Rotation and reuse detection are the right shape, but "already revoked" and "stolen" are not the
/// same event. A person with two tabs open, a reload that fires several calls at once, a request the
/// browser retried after a dropped connection — each of those presents the same refresh token twice
/// within a second or two, entirely legitimately. Treating that as a leak and revoking every token
/// the account has logs the person out everywhere, which is exactly the complaint that made this
/// policy exist.
/// <para>
/// So a replay is only read as a leak once the successor has had time to reach the client:
/// inside <see cref="ReuseGrace"/> of the rotation the caller is simply given a fresh pair, outside
/// it the whole session is revoked as before. The window is short by design — a stolen token is
/// worth a full session, and this hands an attacker at most one pair within seconds of the theft,
/// while the legitimate races above all resolve in well under a second.
/// </para>
/// </remarks>
public interface IRefreshRotationPolicy
{
    /// <summary>How long after a token was rotated away presenting it again is still treated as a race.</summary>
    TimeSpan ReuseGrace { get; }
}
