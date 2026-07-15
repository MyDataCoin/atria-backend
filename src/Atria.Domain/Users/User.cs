using Atria.Domain.Common;

namespace Atria.Domain.Users;

/// <summary>
/// Account aggregate. Authentication is phone-only: accounts are created from a verified
/// Kyrgyzstan phone number (Investor). Verification flags and soft-delete are driven by
/// explicit domain operations; timestamps come from the persistence layer.
/// </summary>
public sealed class User : AggregateRoot
{
    public string? PhoneNumber { get; private set; }
    public Role Role { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsPhoneVerified { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    /// <summary>
    /// Whether the account is banned by a super admin. A banned account is refused a token at login
    /// (investor OTP, admin/realtor credentials) even though the row is otherwise untouched.
    /// </summary>
    public bool IsBanned { get; private set; }

    /// <summary>
    /// BCrypt password hash for credential-login roles (Admin/Realtor/SuperAdmin). Null for
    /// phone-OTP investors, who have no password.
    /// </summary>
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// Set when a super admin resets the password to a temporary one, signalling the account must
    /// change it on next use. Cleared by a restore.
    /// </summary>
    public bool MustResetPassword { get; private set; }

    // private ctor: creation only through the static factory methods
    private User() { }

    /// <summary>Creates an Investor account from a (verified) phone number.</summary>
    public static User CreateFromPhone(string phoneNumber, Role role)
        => new()
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            Role = role,
            IsActive = true
        };

    /// <summary>
    /// Creates a credential-login service account (Admin/Realtor/SuperAdmin) with a fixed id and a
    /// pre-computed password hash. No phone number — these sign in with username/password, not OTP.
    /// Used to seed the configured service accounts so ban/password operations can target them by id.
    /// </summary>
    public static User CreateServiceAccount(Guid id, Role role, string passwordHash)
    {
        if (id == Guid.Empty)
            throw new DomainException("Service account id is required.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Service account password hash is required.");

        return new User
        {
            Id = id,
            Role = role,
            IsActive = true,
            PasswordHash = passwordHash
        };
    }

    public void MarkPhoneVerified() => IsPhoneVerified = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Bans the account (idempotent). A banned account cannot obtain a token.</summary>
    public void Ban() => IsBanned = true;

    /// <summary>Lifts a ban (idempotent).</summary>
    public void Unban() => IsBanned = false;

    /// <summary>
    /// Sets a new password hash. Only credential-login roles have passwords; setting one on an
    /// investor is a programming error. <paramref name="mustReset"/> flags a super-admin reset.
    /// </summary>
    public void SetPassword(string passwordHash, bool mustReset)
    {
        if (Role is not (Role.Admin or Role.Realtor or Role.SuperAdmin))
            throw new DomainException("Only admin and realtor accounts have a password.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Password hash is required.");

        PasswordHash = passwordHash;
        MustResetPassword = mustReset;
    }

    /// <summary>
    /// Clears the forced-reset flag, restoring normal access. Only valid for a credential-login
    /// account that currently requires a reset.
    /// </summary>
    public void RestorePassword()
    {
        if (Role is not (Role.Admin or Role.Realtor or Role.SuperAdmin))
            throw new DomainException("Only admin and realtor accounts have a password.");
        if (!MustResetPassword)
            throw new DomainException("The account does not require a password reset.");

        MustResetPassword = false;
    }

    /// <summary>Soft-deletes the account: records the timestamp and deactivates it.</summary>
    public void SoftDelete(DateTime utc)
    {
        DeletedAtUtc = utc;
        IsActive = false;
    }
}
