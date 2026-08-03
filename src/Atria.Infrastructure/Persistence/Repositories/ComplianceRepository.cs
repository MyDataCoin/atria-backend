using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class ComplianceRepository : Repository<ComplianceProfile>, IComplianceRepository
{
    public ComplianceRepository(AtriaDbContext db) : base(db) { }

    public Task<ComplianceProfile?> GetByInvestorAsync(Guid investorId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(c => c.InvestorId == investorId, ct);

    // Read-only: the caller only needs the investor behind the address.
    public Task<ComplianceProfile?> GetByWalletAsync(string walletAddress, CancellationToken ct)
        => Set.AsNoTracking().FirstOrDefaultAsync(c => c.WalletAddress == walletAddress, ct);
}
