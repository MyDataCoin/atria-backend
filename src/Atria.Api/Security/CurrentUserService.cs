using System.Security.Claims;
using Atria.Application.Abstractions;
using Atria.Domain.Users;

namespace Atria.Api.Security;

/// <summary>
/// Reads the authenticated principal off the current HTTP request so that handlers
/// can run resource-based authorization (an Investor may only touch their OWN rows).
/// Pure adapter over <see cref="IHttpContextAccessor"/> — no business logic.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            // "sub" is the standard JWT subject; ASP.NET often maps it to NameIdentifier.
            var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Principal?.FindFirstValue("sub")
                      ?? Principal?.FindFirstValue("userid");

            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public Role? Role
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.Role)
                      ?? Principal?.FindFirstValue("role");

            return Enum.TryParse<Role>(raw, ignoreCase: true, out var role) ? role : null;
        }
    }

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// True when the principal carries <paramref name="role"/> among ITS role claims — not just as
    /// the first one. <see cref="Role"/> reads a single claim, while <c>[Authorize(Roles = "...")]</c>
    /// matches any of them; deriving this check from <see cref="Role"/> made the two disagree the
    /// moment a token carried more than one role, letting an endpoint admit a caller that the
    /// resource check inside the handler then treated as someone else.
    /// </summary>
    public bool IsInRole(Role role) => Principal?.IsInRole(role.ToString()) ?? false;
}
