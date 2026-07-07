using Atria.Application.Abstractions;
using Atria.Domain.Consents;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class ConsentRepository : Repository<Consent>, IConsentRepository
{
    public ConsentRepository(AtriaDbContext db) : base(db) { }

    public Task<Consent?> GetAsync(Guid userId, ConsentType type, string version, CancellationToken ct)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Type == type && c.Version == version, ct);
}
