using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Commands;

/// <summary>Logs a realtor in with the static username/password and returns a token pair.</summary>
public sealed record RealtorLoginCommand(string Username, string Password) : IRequest<Result<AuthTokensDto>>;

/// <summary>
/// Verifies the static realtor credentials (from configuration, constant-time) and, on success,
/// issues a Realtor access token + refresh token for the configured realtor user id. Returns a
/// generic 401 for both a disabled feature and bad credentials so nothing is leaked.
/// </summary>
public sealed class RealtorLoginCommandHandler : IRequestHandler<RealtorLoginCommand, Result<AuthTokensDto>>
{
    private readonly IRealtorAuthenticator _realtor;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public RealtorLoginCommandHandler(
        IRealtorAuthenticator realtor,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork)
    {
        _realtor = realtor;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AuthTokensDto>> Handle(RealtorLoginCommand request, CancellationToken ct)
    {
        if (!_realtor.IsEnabled || !_realtor.Validate(request.Username, request.Password))
            return Result.Failure<AuthTokensDto>(
                Error.Unauthorized("auth.invalid_credentials", "Invalid username or password."));

        // Issue a Realtor token pair for the configured realtor user id (its 'sub').
        var access = _jwt.GenerateAccessToken(_realtor.RealtorUserId, request.Username, Role.Realtor);
        var refresh = _jwt.GenerateRefreshToken();
        await _refreshTokens.StoreAsync(_realtor.RealtorUserId, refresh.Token, refresh.ExpiresAtUtc, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new AuthTokensDto(access.Token, access.ExpiresAtUtc, refresh.Token));
    }
}
