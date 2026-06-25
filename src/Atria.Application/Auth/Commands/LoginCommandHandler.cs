using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Verifies the password hash; returns the same generic error for unknown user / bad password.</summary>
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthTokensDto>>
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Invalid email or password.");

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public LoginCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AuthTokensDto>> Handle(LoginCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _users.GetByEmailAsync(email, ct);
        // Same error whether the user is missing, inactive, or has no password — no enumeration.
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
            return Result.Failure<AuthTokensDto>(InvalidCredentials);

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            return Result.Failure<AuthTokensDto>(InvalidCredentials);

        var tokens = await AuthTokensFactory.IssueAsync(user, _jwt, _refreshTokens, _unitOfWork, ct);
        return Result.Success(tokens);
    }
}
