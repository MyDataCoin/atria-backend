using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Atria.Application.Tests.Auth;

/// <summary>
/// Covers super-admin credential validation. The feature is enabled by identity (username + id) so a
/// super admin can exist purely as a seeded users row whose hash is checked at login; the config
/// password is only a fallback and, when empty, never grants access.
/// </summary>
public sealed class SuperAdminAuthenticatorTests
{
    private static SuperAdminAuthenticator Auth(string username, string password, Guid id)
        => new(Options.Create(new SuperAdminOptions { Username = username, Password = password, UserId = id }));

    [Fact]
    public void Enabled_by_username_and_id_even_without_a_password()
    {
        var auth = Auth("superadmin", "", Guid.NewGuid());

        auth.IsEnabled.Should().BeTrue();
        auth.MatchesUsername("superadmin").Should().BeTrue("login routes by username; the hash is checked later");
    }

    [Fact]
    public void Disabled_without_a_username_or_id()
    {
        Auth("", "pass", Guid.NewGuid()).IsEnabled.Should().BeFalse();
        Auth("superadmin", "pass", Guid.Empty).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Empty_config_password_never_validates()
    {
        var auth = Auth("superadmin", "", Guid.NewGuid());

        // A blank password must not slip through the config-fallback path.
        auth.Validate("superadmin", "").Should().BeFalse();
        auth.Validate("superadmin", "anything").Should().BeFalse();
    }

    [Fact]
    public void Config_password_validates_when_set()
    {
        var auth = Auth("superadmin", "s3cret", Guid.NewGuid());

        auth.Validate("superadmin", "s3cret").Should().BeTrue();
        auth.Validate("superadmin", "wrong").Should().BeFalse();
        auth.Validate("someone", "s3cret").Should().BeFalse();
    }
}
