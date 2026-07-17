using Atria.Domain.Common;
using Atria.Domain.Users;
using FluentAssertions;

namespace Atria.Domain.Tests.Users;

/// <summary>
/// Covers the super-admin-facing account operations on <see cref="User"/>: ban/unban idempotency,
/// password set (only for credential-login roles), and the forced-reset restore flow.
/// </summary>
public sealed class UserBanAndPasswordTests
{
    private static User Admin() => User.CreateServiceAccount("admin", Role.Admin, "hash");
    private static User Investor() => User.CreateFromPhone("+996700123456", Role.Investor);

    [Fact]
    public void Ban_and_unban_are_idempotent()
    {
        var user = Investor();
        user.IsBanned.Should().BeFalse();

        user.Ban();
        user.Ban();
        user.IsBanned.Should().BeTrue();

        user.Unban();
        user.Unban();
        user.IsBanned.Should().BeFalse();
    }

    [Fact]
    public void SetPassword_sets_hash_and_reset_flag_for_admin()
    {
        var user = Admin();

        user.SetPassword("new-hash", mustReset: true);

        user.PasswordHash.Should().Be("new-hash");
        user.MustResetPassword.Should().BeTrue();
    }

    [Fact]
    public void SetPassword_rejects_an_investor()
    {
        var act = () => Investor().SetPassword("hash", mustReset: true);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void RestorePassword_clears_the_reset_flag()
    {
        var user = Admin();
        user.SetPassword("hash", mustReset: true);

        user.RestorePassword();

        user.MustResetPassword.Should().BeFalse();
    }

    [Fact]
    public void RestorePassword_throws_when_no_reset_pending()
    {
        var act = () => Admin().RestorePassword();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void CreateServiceAccount_sets_username_role_hash_active_and_no_phone()
    {
        var user = User.CreateServiceAccount("superadmin", Role.SuperAdmin, "hash");

        user.Username.Should().Be("superadmin");
        user.Role.Should().Be(Role.SuperAdmin);
        user.PasswordHash.Should().Be("hash");
        user.IsActive.Should().BeTrue();
        user.PhoneNumber.Should().BeNull();
        user.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void CreateServiceAccount_rejects_a_non_credential_role()
    {
        var act = () => User.CreateServiceAccount("someone", Role.Investor, "hash");
        act.Should().Throw<DomainException>();
    }
}
