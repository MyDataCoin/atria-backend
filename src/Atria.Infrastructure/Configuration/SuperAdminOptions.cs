namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Static super-admin credentials for the shared password-login endpoint (<c>/auth/admin/login</c>).
/// The feature is DISABLED when <see cref="Password"/> is empty. Supply real values via environment
/// variables (<c>SuperAdmin__Username</c> / <c>SuperAdmin__Password</c> / <c>SuperAdmin__UserId</c>)
/// — never commit them.
/// </summary>
public sealed class SuperAdminOptions
{
    public const string SectionName = "SuperAdmin";

    /// <summary>The super-admin login name.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>The static super-admin password. Empty disables super-admin login entirely.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Id of the super-admin <c>users</c> row; becomes the token <c>sub</c>. MUST match the seeded
    /// SuperAdmin account so the issued JWT and its refresh token map to a real user.
    /// </summary>
    public Guid UserId { get; init; }
}
