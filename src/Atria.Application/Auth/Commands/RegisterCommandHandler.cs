using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Commands;

/// <summary>Rejects duplicate emails, hashes the password, creates an Investor, issues tokens.</summary>
public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, Result<AuthTokensDto>>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterCommandHandler(
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

    public async Task<Result<AuthTokensDto>> Handle(RegisterCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var existing = await _users.GetByEmailAsync(email, ct);
        if (existing is not null)
            return Result.Failure<AuthTokensDto>(
                Error.Conflict("auth.email_taken", "An account with this email already exists."));

        var hash = _passwordHasher.Hash(request.Password);
        var user = User.CreateWithPassword(email, hash, Role.Investor, request.FirstName, request.LastName);

        await _users.AddAsync(user, ct);

        // IssueAsync commits the user + the refresh token together (atomic).
        var tokens = await AuthTokensFactory.IssueAsync(user, _jwt, _refreshTokens, _unitOfWork, ct);
        return Result.Success(tokens);
    }
}
