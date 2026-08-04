using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Stores;

/// <summary>
/// EF-backed refresh token store. Only the SHA-256 hash of the raw token is
/// persisted; lookups hash the supplied token before querying so the plaintext
/// is never stored or compared directly.
/// </summary>
public sealed class RefreshTokenStore : IRefreshTokenStore
{
    private readonly AtriaDbContext _db;

    public RefreshTokenStore(AtriaDbContext db) => _db = db;

    public async Task StoreAsync(Guid userId, string refreshToken, DateTime expiresAtUtc, CancellationToken ct)
        => await _db.RefreshTokens.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(refreshToken),
            ExpiresAtUtc = expiresAtUtc,
            IsRevoked = false,
            CreatedAtUtc = DateTime.UtcNow
        }, ct);

    public async Task<RefreshTokenInfo?> FindAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var entity = await _db.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TokenHash == hash, ct);

        return entity is null
            ? null
            : new RefreshTokenInfo(entity.UserId, refreshToken, entity.ExpiresAtUtc, entity.IsRevoked);
    }

    public async Task<bool> TryRevokeAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);

        // Conditional update executed BY THE DATABASE: of two concurrent refreshes presenting the
        // same token, exactly one flips the flag and the other is told it lost. Reading the row and
        // then setting a property instead lets both callers pass the not-revoked check and rotate
        // one token into two live sessions.
        if (SupportsBulkOperations)
        {
            var affected = await _db.RefreshTokens
                .Where(r => r.TokenHash == hash && !r.IsRevoked)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.IsRevoked, true), ct);

            return affected == 1;
        }

        // The in-memory provider used by the test host has no bulk operations. It also has no
        // concurrency to protect against, being a single process with a serialized store, so the
        // tracked equivalent is behaviourally identical there.
        var entity = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (entity is null || entity.IsRevoked)
            return false;

        entity.IsRevoked = true;
        return true;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
        => await TryRevokeAsync(refreshToken, ct);

    public async Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken ct)
    {
        // Expired rows are only ever read by hash and every path re-checks the expiry, so nothing
        // needs them once they can no longer authenticate anything. Without this the table grows for
        // the life of the product — rotation writes a row per refresh.
        if (SupportsBulkOperations)
        {
            return await _db.RefreshTokens
                .Where(r => r.ExpiresAtUtc < olderThanUtc)
                .ExecuteDeleteAsync(ct);
        }

        var stale = await _db.RefreshTokens.Where(r => r.ExpiresAtUtc < olderThanUtc).ToListAsync(ct);
        _db.RefreshTokens.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
        return stale.Count;
    }

    /// <summary>False for the in-memory provider, which implements neither ExecuteUpdate nor ExecuteDelete.</summary>
    private bool SupportsBulkOperations => _db.Database.IsRelational();

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var tokens = await _db.RefreshTokens
            .Where(r => r.UserId == userId && !r.IsRevoked)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.IsRevoked = true;
    }

    // SHA-256 hex of the raw token; deterministic so lookups match what was stored.
    private static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
