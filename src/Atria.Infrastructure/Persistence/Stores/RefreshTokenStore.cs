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

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var entity = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (entity is not null)
            entity.IsRevoked = true;
    }

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
