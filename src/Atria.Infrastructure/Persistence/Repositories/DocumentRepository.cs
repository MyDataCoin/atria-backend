using Atria.Application.Abstractions;
using Atria.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class DocumentRepository : Repository<DocumentRecord>, IDocumentRepository
{
    public DocumentRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<DocumentRecord>> GetByOwnerAsync(Guid ownerId, CancellationToken ct)
        => await Set.AsNoTracking().Where(d => d.OwnerUserId == ownerId).ToListAsync(ct);
}
