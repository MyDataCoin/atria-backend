using Atria.Application.Abstractions;
using Atria.Domain.Kyc;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class KycRepository : Repository<KycProfile>, IKycRepository
{
    public KycRepository(AtriaDbContext db) : base(db) { }

    public Task<KycProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(k => k.UserId == userId, ct);

    public Task<KycProfile?> GetBySessionIdAsync(string sessionId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(k => k.ProviderSessionId == sessionId, ct);
}
