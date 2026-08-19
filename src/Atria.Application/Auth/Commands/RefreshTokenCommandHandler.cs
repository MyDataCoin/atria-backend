using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>
/// Rotating refresh: look up the presented token, revoke it and issue a brand-new pair. A token
/// that was already rotated away is a race inside <see cref="IRefreshRotationPolicy.ReuseGrace"/>
/// (answered with a fresh pair) and reuse of a leaked token after it (the whole session is revoked).
/// </summary>
public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthTokensDto>>
{
    private static readonly Error InvalidToken =
        Error.Unauthorized("auth.invalid_refresh_token", "The refresh token is invalid or expired.");

    private readonly IUserRepository _users;
    private readonly IRefreshRotationPolicy _rotation;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RefreshTokenCommandHandler(
        IUserRepository users,
        IRefreshTokenStore refreshTokens,
        IJwtTokenGenerator jwt,
        IDateTimeProvider clock,
        IRefreshRotationPolicy rotation,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _rotation = rotation;
        _refreshTokens = refreshTokens;
        _jwt = jwt;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AuthTokensDto>> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        var info = await _refreshTokens.FindAsync(request.RefreshToken, ct);

        // Not found at all: nothing to rotate, can't attribute to a user.
        if (info is null)
            return Result.Failure<AuthTokensDto>(InvalidToken);

        // Revoked token presented again. Two very different things look identical here: a second tab
        // (or a retried request, or several calls that 401-ed together) presenting the token a beat
        // after it was rotated, and an attacker replaying a token they stole. The revocation instant
        // separates them — inside the grace it is a race and the caller gets a working pair, after it
        // the session is treated as compromised and every token for the account is revoked.
        if (info.IsRevoked)
        {
            if (IsRotationRace(info))
                return await IssueForActiveUserAsync(info.UserId, ct);

            await _refreshTokens.RevokeAllForUserAsync(info.UserId, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<AuthTokensDto>(InvalidToken);
        }

        // Expired token: revoke just this one.
        if (info.ExpiresAtUtc <= _clock.UtcNow)
        {
            await _refreshTokens.RevokeAsync(request.RefreshToken, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<AuthTokensDto>(InvalidToken);
        }

        var user = await _users.GetByIdAsync(info.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await _refreshTokens.RevokeAsync(request.RefreshToken, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<AuthTokensDto>(InvalidToken);
        }

        // Rotate. The revoke is a compare-and-set rather than a read-then-write: two requests
        // presenting the same token concurrently — which the dashboards do whenever several calls
        // 401 at once — would otherwise both pass the IsRevoked check above and both mint a pair,
        // turning one token into two live sessions. Only the caller that actually flipped the flag
        // rotates; the loser lost by microseconds, so it is served a pair of its own rather than
        // being thrown back to the login screen for having been a moment late.
        if (!await _refreshTokens.TryRevokeAsync(request.RefreshToken, ct))
        {
            await _unitOfWork.SaveChangesAsync(ct);

            var raced = await _refreshTokens.FindAsync(request.RefreshToken, ct);
            if (raced is not null && IsRotationRace(raced))
                return await IssueForActiveUserAsync(raced.UserId, ct);

            return Result.Failure<AuthTokensDto>(InvalidToken);
        }

        var tokens = await AuthTokensFactory.IssueAsync(user, _jwt, _refreshTokens, _unitOfWork, ct);
        return Result.Success(tokens);
    }

    /// <summary>Was this token rotated away just now, rather than long enough ago to be a replay?</summary>
    /// <remarks>
    /// A revoked row with no timestamp predates the column, so it cannot be shown to be recent and is
    /// treated as a replay — the safe reading.
    /// </remarks>
    private bool IsRotationRace(RefreshTokenInfo info)
        => info.RevokedAtUtc is { } revokedAt
           && _clock.UtcNow - DateTime.SpecifyKind(revokedAt, DateTimeKind.Utc) <= _rotation.ReuseGrace
           && info.ExpiresAtUtc > _clock.UtcNow;

    /// <summary>Issues a pair for a user resolved from a token, refusing accounts that are gone or banned.</summary>
    private async Task<Result<AuthTokensDto>> IssueForActiveUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive)
        {
            await _refreshTokens.RevokeAllForUserAsync(userId, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<AuthTokensDto>(InvalidToken);
        }

        return Result.Success(await AuthTokensFactory.IssueAsync(user, _jwt, _refreshTokens, _unitOfWork, ct));
    }
}
