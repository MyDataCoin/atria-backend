using Atria.Domain.Users;

namespace Atria.Application.Abstractions;

/// <summary>
/// The authenticated principal of the current request. Used for resource-based
/// authorization in handlers (an investor may only touch their OWN resources).
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    Role? Role { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(Role role);
}
