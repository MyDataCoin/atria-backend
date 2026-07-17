using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Logs a realtor in with a username/password and returns a token pair.</summary>
public sealed record RealtorLoginCommand(string Username, string Password) : IRequest<Result<AuthTokensDto>>;

/// <summary>
/// Credential login for the realtor endpoint. Looks the account up by username in the database and
/// verifies the password against its stored hash; the role comes from the row. No configuration is
/// involved — accounts live only in <c>users</c>. A missing/wrong/banned account yields a generic 401.
/// </summary>
public sealed class RealtorLoginCommandHandler : IRequestHandler<RealtorLoginCommand, Result<AuthTokensDto>>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public RealtorLoginCommandHandler(
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

    public Task<Result<AuthTokensDto>> Handle(RealtorLoginCommand request, CancellationToken ct)
        => AuthTokensFactory.IssueForCredentialLoginAsync(
            request.Username, request.Password,
            _users, _passwordHasher, _jwt, _refreshTokens, _unitOfWork, ct);
}
