using Atria.Domain.Investments;
using Atria.Domain.Kyc;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="Investment"/> with investor-scoped lookups.</summary>
public interface IInvestmentRepository : IRepository<Investment>
{
    Task<IReadOnlyList<Investment>> GetByInvestorAsync(Guid investorId, CancellationToken ct);

    /// <summary>DB-side aggregate of the investor's Active investments: total invested and active count.</summary>
    Task<(decimal TotalInvested, int ActiveCount)> GetActiveTotalsAsync(Guid investorId, CancellationToken ct);

    /// <summary>
    /// Active investments in a property with their token counts and the investor's (optional) KYC
    /// profile. The KYC entity is materialized so its encrypted FullName is decrypted by the value
    /// converter. Admin/Compliance reporting read.
    /// </summary>
    Task<IReadOnlyList<(Guid InvestorId, long TokenCount, KycProfile? Kyc)>>
        GetActiveByPropertyAsync(Guid propertyId, CancellationToken ct);
}
