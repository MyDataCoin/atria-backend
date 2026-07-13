namespace Atria.Application.Abstractions;

/// <summary>
/// Validates the static realtor username/password (from configuration) for the realtor login
/// endpoint. Implemented in Infrastructure so the Application layer stays free of config.
/// Realtors have no SMS login; they sign in with credentials like the admin.
/// </summary>
public interface IRealtorAuthenticator
{
    /// <summary>True only when a static realtor password is configured (feature enabled).</summary>
    bool IsEnabled { get; }

    /// <summary>The configured realtor user id, used as the token subject.</summary>
    Guid RealtorUserId { get; }

    /// <summary>Constant-time check of the supplied credentials against configuration.</summary>
    bool Validate(string username, string password);
}
