using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>
/// Rotating refresh: look up the presented token; a missing or already-revoked token is
/// treated as reuse of a leaked token, so the whole user session is revoked. Otherwise
/// revoke the old token and issue a brand-new pair.
/// </summary>
public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthTokensDto>>
{
    private static readonly Error InvalidToken =
        Error.Unauthorized("auth.invalid_refresh_token", "The refresh token is invalid or expired.");

    private readonly IUserRepository _users;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RefreshTokenCommandHandler(
        IUserRepository users,
        IRefreshTokenStore refreshTokens,
        IJwtTokenGenerator jwt,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _users = users;
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

        // Revoked token presented again => reuse of a leaked token: nuke the whole session.
        if (info.IsRevoked)
        {
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

        // Rotate: revoke the old token, then issue + store the new pair (IssueAsync commits both).
        await _refreshTokens.RevokeAsync(request.RefreshToken, ct);

        var tokens = await AuthTokensFactory.IssueAsync(user, _jwt, _refreshTokens, _unitOfWork, ct);
        return Result.Success(tokens);
    }
}
