using System.ComponentModel.DataAnnotations;
using Atria.Application.Abstractions;

namespace Atria.Infrastructure.Configuration;

/// <summary>Bound form of <see cref="IRefreshRotationPolicy"/> (configuration section "Auth:RefreshRotation").</summary>
public sealed class RefreshRotationOptions : IRefreshRotationPolicy
{
    public const string SectionName = "Auth:RefreshRotation";

    /// <summary>
    /// Seconds after a rotation during which the old token is still accepted (and answered with a
    /// fresh pair) instead of being read as a leak. 0 restores the old all-or-nothing behaviour.
    /// </summary>
    [Range(0, 300)]
    public int ReuseGraceSeconds { get; init; } = 60;

    /// <inheritdoc />
    TimeSpan IRefreshRotationPolicy.ReuseGrace => TimeSpan.FromSeconds(ReuseGraceSeconds);
}
