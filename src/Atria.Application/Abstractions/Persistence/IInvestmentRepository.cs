using Atria.Domain.Investments;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="Investment"/> with investor-scoped lookups.</summary>
public interface IInvestmentRepository : IRepository<Investment>
{
    Task<IReadOnlyList<Investment>> GetByInvestorAsync(Guid investorId, CancellationToken ct);

    /// <summary>DB-side aggregate of the investor's Active investments: total invested and active count.</summary>
    Task<(decimal TotalInvested, int ActiveCount)> GetActiveTotalsAsync(Guid investorId, CancellationToken ct);
}
