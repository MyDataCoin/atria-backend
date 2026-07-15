namespace Atria.Application.Abstractions;

/// <summary>
/// Validates the static admin username/password (from configuration) for the admin login
/// endpoint. Implemented in Infrastructure so the Application layer stays free of config.
/// </summary>
public interface IAdminAuthenticator
{
    /// <summary>True only when a static admin password is configured (feature enabled).</summary>
    bool IsEnabled { get; }

    /// <summary>The configured admin user id, used as the token subject.</summary>
    Guid AdminUserId { get; }

    /// <summary>Constant-time check of the supplied credentials against configuration.</summary>
    bool Validate(string username, string password);

    /// <summary>
    /// Constant-time check of just the username against configuration (feature must be enabled).
    /// Used to route a login to this identity before the password is verified against the stored
    /// hash, so a reset password (which no longer matches config) still logs in.
    /// </summary>
    bool MatchesUsername(string username);
}
