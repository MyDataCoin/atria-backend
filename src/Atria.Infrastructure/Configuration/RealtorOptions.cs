namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Static realtor credentials for the password-login endpoint. The feature is DISABLED when
/// <see cref="Password"/> is empty. Supply real values via environment variables
/// (<c>Realtor__Username</c> / <c>Realtor__Password</c> / <c>Realtor__UserId</c>) — never commit them.
/// </summary>
public sealed class RealtorOptions
{
    public const string SectionName = "Realtor";

    /// <summary>The realtor login name.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>The static realtor password. Empty disables realtor login entirely.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Id of the realtor <c>users</c> row; becomes the token <c>sub</c>. MUST match the seeded
    /// Realtor account so the issued JWT and its refresh token map to a real user.
    /// </summary>
    public Guid UserId { get; init; }
}
