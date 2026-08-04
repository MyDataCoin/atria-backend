using Atria.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Stores;

/// <summary>
/// EF-backed OTP store. Codes are persisted only as hashes (hashing is done by the
/// OTP service). Supports lookup of the latest active code and request-rate counting.
/// </summary>
public sealed class OtpCodeStore : IOtpCodeStore
{
    private readonly AtriaDbContext _db;

    public OtpCodeStore(AtriaDbContext db) => _db = db;

    public async Task AddAsync(
        string phone, string codeHash, DateTime expiresAtUtc, string? requestedFromIp, CancellationToken ct)
        => await _db.OtpCodes.AddAsync(new OtpCode
        {
            Id = Guid.NewGuid(),
            Phone = phone,
            CodeHash = codeHash,
            ExpiresAtUtc = expiresAtUtc,
            Attempts = 0,
            Consumed = false,
            CreatedAtUtc = DateTime.UtcNow,
            RequestedFromIp = requestedFromIp
        }, ct);

    public async Task<OtpEntry?> GetLatestActiveAsync(string phone, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entity = await _db.OtpCodes.AsNoTracking()
            .Where(o => o.Phone == phone && !o.Consumed && o.ExpiresAtUtc > now)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return entity is null
            ? null
            : new OtpEntry(entity.Id, entity.CodeHash, entity.ExpiresAtUtc, entity.Attempts, entity.Consumed);
    }

    public async Task IncrementAttemptsAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.OtpCodes.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (entity is not null)
            entity.Attempts++;
    }

    public async Task ConsumeAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.OtpCodes.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (entity is not null)
            entity.Consumed = true;
    }

    public Task<int> CountRequestsSinceAsync(string phone, DateTime sinceUtc, CancellationToken ct)
        => _db.OtpCodes.CountAsync(o => o.Phone == phone && o.CreatedAtUtc >= sinceUtc, ct);

    public async Task<(int Requests, int DistinctPhones)> CountRequestsFromIpSinceAsync(
        string ipAddress, DateTime sinceUtc, CancellationToken ct)
    {
        // One pass over the window rather than two round trips: both numbers come from the same rows.
        var phones = await _db.OtpCodes.AsNoTracking()
            .Where(o => o.RequestedFromIp == ipAddress && o.CreatedAtUtc >= sinceUtc)
            .Select(o => o.Phone)
            .ToListAsync(ct);

        return (phones.Count, phones.Distinct(StringComparer.Ordinal).Count());
    }
}
