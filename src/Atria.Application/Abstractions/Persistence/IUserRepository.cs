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
}
