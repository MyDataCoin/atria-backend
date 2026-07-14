using Atria.Domain.Realtors;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="RealtorProfile"/> (one profile per user).</summary>
public interface IRealtorProfileRepository : IRepository<RealtorProfile>
{
    Task<RealtorProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct);
}
