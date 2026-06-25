using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class ComplianceRepository : Repository<ComplianceProfile>, IComplianceRepository
{
    public ComplianceRepository(AtriaDbContext db) : base(db) { }

    public Task<ComplianceProfile?> GetByInvestorAsync(Guid investorId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(c => c.InvestorId == investorId, ct);
}
