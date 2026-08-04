using System.ComponentModel.DataAnnotations;
using Atria.Application.Abstractions;

namespace Atria.Infrastructure.Configuration;

/// <summary>Bound form of <see cref="IAuthLockoutPolicy"/> (configuration section "Auth:Lockout").</summary>
public sealed class AuthLockoutOptions : IAuthLockoutPolicy
{
    public const string SectionName = "Auth:Lockout";

    /// <inheritdoc />
    [Range(3, 1000)]
    public int MaxFailedLogins { get; init; } = 10;

    /// <summary>How long a locked account stays locked, in minutes.</summary>
    [Range(1, 1440)]
    public int LockoutMinutes { get; init; } = 15;

    /// <inheritdoc />
    TimeSpan IAuthLockoutPolicy.LockoutDuration => TimeSpan.FromMinutes(LockoutMinutes);
}
