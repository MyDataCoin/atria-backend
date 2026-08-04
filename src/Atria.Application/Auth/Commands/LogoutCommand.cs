using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Ends a session: revokes the presented refresh token.</summary>
public sealed record LogoutCommand(string RefreshToken) : IRequest<Result>;

/// <summary>
/// Revokes the refresh token behind a session. Always reports success — whether the token existed
/// is not information a caller of an unauthenticated endpoint needs, and there is nothing for them
/// to do differently either way.
/// </summary>
/// <remarks>
/// Signing out has to revoke something server-side. Clearing the client's copy alone leaves the
/// token valid for the rest of its thirty days, so a session ended on a shared machine is not
/// actually ended — anyone who recovered the value could keep rotating it.
/// </remarks>
public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public LogoutCommandHandler(IRefreshTokenStore refreshTokens, IUnitOfWork unitOfWork)
    {
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await _refreshTokens.RevokeAsync(request.RefreshToken, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
