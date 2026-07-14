using Atria.Application.Abstractions;
using Atria.Domain.Support;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class SupportTicketRepository : Repository<SupportTicket>, ISupportTicketRepository
{
    public SupportTicketRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<SupportTicket>> GetAllAsync(TicketStatus? status, CancellationToken ct)
        => await OrderedList(Set.AsNoTracking(), status).ToListAsync(ct);

    public async Task<IReadOnlyList<SupportTicket>> GetByInvestorAsync(
        Guid investorId, TicketStatus? status, CancellationToken ct)
        => await OrderedList(Set.AsNoTracking().Where(t => t.InvestorId == investorId), status).ToListAsync(ct);

    public Task<SupportTicket?> GetByIdWithMessagesAsync(Guid id, CancellationToken ct)
        => Set.AsNoTracking()
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyDictionary<Guid, string?>> GetAuthorNamesAsync(
        IReadOnlyCollection<Guid> authorIds, CancellationToken ct)
    {
        if (authorIds.Count == 0)
            return new Dictionary<Guid, string?>();

        // Materialize the KycProfile entity (not the raw column) so the value converter decrypts
        // FullName in-memory; same approach as InvestmentRepository.GetActiveByPropertyAsync. A
        // realtor has no KYC profile, so its name is simply absent (null).
        var profiles = await Db.KycProfiles.AsNoTracking()
            .Where(k => authorIds.Contains(k.UserId))
            .ToListAsync(ct);

        // One profile per user; if duplicates ever exist, the first wins.
        return profiles
            .GroupBy(k => k.UserId)
            .ToDictionary(g => g.Key, g => g.First().FullName);
    }

    // Newest activity first: last-updated, falling back to creation time for never-touched tickets.
    private static IQueryable<SupportTicket> OrderedList(IQueryable<SupportTicket> query, TicketStatus? status)
    {
        if (status is not null)
            query = query.Where(t => t.Status == status);

        return query.OrderByDescending(t => t.UpdatedAtUtc ?? t.CreatedAtUtc);
    }
}
