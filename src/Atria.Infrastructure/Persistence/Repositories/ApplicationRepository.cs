using Atria.Application.Abstractions;
using Atria.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class ApplicationRepository : Repository<InvestorApplication>, IApplicationRepository
{
    public ApplicationRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<InvestorApplication>> GetByInvestorAsync(Guid investorId, CancellationToken ct)
        => await Set.AsNoTracking().Where(a => a.InvestorId == investorId).ToListAsync(ct);
}
