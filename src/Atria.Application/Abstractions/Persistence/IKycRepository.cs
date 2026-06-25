using Atria.Domain.Kyc;

namespace Atria.Application.Abstractions;

/// <summary>
/// KYC profile aggregate repository. Adds lookup by owning user (one profile per
/// user) and by external provider session id (used by the verified webhook).
/// </summary>
public interface IKycRepository : IRepository<KycProfile>
{
    Task<KycProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task<KycProfile?> GetBySessionIdAsync(string sessionId, CancellationToken ct);
}
