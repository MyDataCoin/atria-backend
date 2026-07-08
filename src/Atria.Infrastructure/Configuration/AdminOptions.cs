namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Static admin credentials for the password-login endpoint. The feature is DISABLED when
/// <see cref="Password"/> is empty. Supply real values via environment variables
/// (<c>Admin__Username</c> / <c>Admin__Password</c> / <c>Admin__UserId</c>) — never commit them.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>The admin login name.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>The static admin password. Empty disables admin login entirely.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Id of the admin <c>users</c> row; becomes the token <c>sub</c>. MUST match the seeded
    /// Admin account so the issued JWT and its refresh token map to a real user.
    /// </summary>
    public Guid UserId { get; init; }
}
