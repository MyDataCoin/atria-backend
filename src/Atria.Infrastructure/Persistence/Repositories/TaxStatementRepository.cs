using Atria.Application.Abstractions;
using Atria.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class TaxStatementRepository : Repository<TaxStatement>, ITaxStatementRepository
{
    public TaxStatementRepository(AtriaDbContext db) : base(db) { }

    public Task<TaxStatement?> FindAsync(Guid investorId, int year, CancellationToken ct)
        => Set.AsNoTracking().FirstOrDefaultAsync(s => s.InvestorId == investorId && s.Year == year, ct);

    public async Task<IReadOnlyList<TaxStatement>> ListByInvestorAsync(Guid investorId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(s => s.InvestorId == investorId)
            .OrderByDescending(s => s.Year)
            .ToListAsync(ct);

    public Task<TaxStatement?> FindByVerificationCodeAsync(string verificationCode, CancellationToken ct)
        => Set.AsNoTracking().FirstOrDefaultAsync(s => s.VerificationCode == verificationCode, ct);
}
