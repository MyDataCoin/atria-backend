using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Commands;

/// <summary>Logs an admin in with the static username/password and returns a token pair.</summary>
public sealed record AdminLoginCommand(string Username, string Password) : IRequest<Result<AuthTokensDto>>;

/// <summary>
/// Verifies the static admin credentials (from configuration, constant-time) and, on success,
/// issues an Admin access token + refresh token for the configured admin user id. Returns a
/// generic 401 for both a disabled feature and bad credentials so nothing is leaked.
/// </summary>
public sealed class AdminLoginCommandHandler : IRequestHandler<AdminLoginCommand, Result<AuthTokensDto>>
{
    private readonly IAdminAuthenticator _admin;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public AdminLoginCommandHandler(
        IAdminAuthenticator admin,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork)
    {
        _admin = admin;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AuthTokensDto>> Handle(AdminLoginCommand request, CancellationToken ct)
    {
        if (!_admin.IsEnabled || !_admin.Validate(request.Username, request.Password))
            return Result.Failure<AuthTokensDto>(
                Error.Unauthorized("auth.invalid_credentials", "Invalid username or password."));

        // Issue an Admin token pair for the configured admin user id (its 'sub').
        var access = _jwt.GenerateAccessToken(_admin.AdminUserId, request.Username, Role.Admin);
        var refresh = _jwt.GenerateRefreshToken();
        await _refreshTokens.StoreAsync(_admin.AdminUserId, refresh.Token, refresh.ExpiresAtUtc, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new AuthTokensDto(access.Token, access.ExpiresAtUtc, refresh.Token));
    }
}
