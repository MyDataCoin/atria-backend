using Atria.Application.Abstractions;
using Atria.Domain.Feedback;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class FeedbackRequestRepository : Repository<FeedbackRequest>, IFeedbackRequestRepository
{
    public FeedbackRequestRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<FeedbackRequest>> GetAllAsync(CancellationToken ct)
        => await Db.FeedbackRequests
            .AsNoTracking()
            .OrderByDescending(f => f.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<int> DeleteOlderThanAsync(DateTime olderThanUtc, CancellationToken ct)
        => Db.FeedbackRequests
            .Where(f => f.CreatedAtUtc < olderThanUtc)
            .ExecuteDeleteAsync(ct);
}
