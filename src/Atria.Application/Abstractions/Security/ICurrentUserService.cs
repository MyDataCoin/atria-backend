using Atria.Domain.Users;

namespace Atria.Application.Abstractions;

/// <summary>
/// The authenticated principal of the current request. Used for resource-based
/// authorization in handlers (an investor may only touch their OWN resources).
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }

    // No email/phone here on purpose. The token is stored client-side, so every claim in it is a
    // claim published to whoever gets hold of it; the user id is enough to look the rest up.
    Role? Role { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(Role role);
}
