using Atria.Domain.Consents;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="Consent"/> with per-user/type/version lookup.</summary>
public interface IConsentRepository : IRepository<Consent>
{
    /// <summary>The user's recorded acceptance of a specific consent type + version, or null.</summary>
    Task<Consent?> GetAsync(Guid userId, ConsentType type, string version, CancellationToken ct);
}
