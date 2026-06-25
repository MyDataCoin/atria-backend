using Atria.Domain.Compliance;

namespace Atria.Application.Abstractions;

/// <summary>
/// Specialized repository for <see cref="ComplianceProfile"/> aggregates. Adds a
/// lookup by investor id on top of the generic <see cref="IRepository{TEntity}"/>.
/// </summary>
public interface IComplianceRepository : IRepository<ComplianceProfile>
{
    /// <summary>Returns the investor's compliance profile, or null if none exists.</summary>
    Task<ComplianceProfile?> GetByInvestorAsync(Guid investorId, CancellationToken ct);
}
