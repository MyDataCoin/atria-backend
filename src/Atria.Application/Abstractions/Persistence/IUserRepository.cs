using Atria.Domain.Kyc;
using Atria.Domain.Users;

namespace Atria.Application.Abstractions;

/// <summary>
/// User aggregate repository. Lookup is by phone number — the sole login identifier
/// (accounts authenticate via phone OTP).
/// </summary>
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByPhoneAsync(string phone, CancellationToken ct);

    /// <summary>Number of active (non-deleted) users in a role. Used for headline dashboard counters.</summary>
    Task<int> CountByRoleAsync(Role role, CancellationToken ct);

    /// <summary>Ids of every active (non-deleted) user in a role. Used to fan out notifications.</summary>
    Task<IReadOnlyList<Guid>> GetIdsByRoleAsync(Role role, CancellationToken ct);

    /// <summary>
    /// All users left-joined to their (optional) KYC profile, newest first. The KYC entity is
    /// materialized so its encrypted FullName is decrypted by the value converter; the profile is
    /// null for users without one. Admin/Compliance reporting read.
    /// </summary>
    Task<IReadOnlyList<(User User, KycProfile? Kyc)>> GetOverviewAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a credential-login service account (Admin/Realtor/SuperAdmin) exists as a persisted
    /// row with the given id, role and password hash, and returns it. Creates + commits it when
    /// absent; when it already exists, returns it and backfills the hash if it had none. Tolerant of a
    /// concurrent caller racing the same insert (the row exists either way). Used so a service account
    /// self-provisions on first login without manual SQL or startup seeding.
    /// </summary>
    Task<User> EnsureServiceAccountAsync(Guid id, Role role, string passwordHash, CancellationToken ct);
}
