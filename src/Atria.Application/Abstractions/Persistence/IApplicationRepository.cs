using Atria.Domain.Applications;

namespace Atria.Application.Abstractions;

/// <summary>
/// Specialized repository for <see cref="InvestorApplication"/> aggregates.
/// Extends the generic repository with an investor-scoped query.
/// </summary>
public interface IApplicationRepository : IRepository<InvestorApplication>
{
    Task<IReadOnlyList<InvestorApplication>> GetByInvestorAsync(Guid investorId, CancellationToken ct);
}
