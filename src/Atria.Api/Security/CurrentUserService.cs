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

    public string? Email
        => Principal?.FindFirstValue(ClaimTypes.Email)
           ?? Principal?.FindFirstValue("email");

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

    public bool IsInRole(Role role) => Role == role;
}
