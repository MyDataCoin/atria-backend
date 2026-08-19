using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Users.Dtos;

namespace Atria.Application.Users.Queries;

/// <summary>Returns the authenticated caller's own account row. Any authenticated role.</summary>
public sealed record GetCurrentUserQuery : IRequest<Result<CurrentUserDto>>;

/// <summary>
/// Projects the caller's <c>users</c> row into the header/session shape the panels read right after
/// a login: display name, login, and the pending-password-change flag.
/// </summary>
public sealed class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, Result<CurrentUserDto>>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IUserRepository _users;

    public GetCurrentUserQueryHandler(ICurrentUserService currentUser, IUserRepository users)
    {
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<Result<CurrentUserDto>> Handle(GetCurrentUserQuery request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<CurrentUserDto>(
                Error.Unauthorized("auth.unauthenticated", "Not authenticated."));

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
            return Result.Failure<CurrentUserDto>(Error.NotFound("user.not_found", "User not found."));

        return Result.Success(new CurrentUserDto(
            user.Id, user.Role.ToString(), user.Username, user.FullName, user.MustResetPassword));
    }
}
