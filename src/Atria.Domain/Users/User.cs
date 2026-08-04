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

    /// <summary>
    /// Login name for credential accounts (Admin/Realtor/SuperAdmin). Null for phone-OTP investors and
    /// ordinary realtors (who have no login) — they are identified by phone, not a username.
    /// </summary>
    public string? Username { get; private set; }

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
    /// Optional reason a super admin gave when banning the account. Shown to the user on the blocked
    /// screen (surfaced as <c>banReason</c> in the 403 login response). Null when no reason was given
    /// or the account is not banned; cleared on unban.
    /// </summary>
    public string? BanReason { get; private set; }

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

    /// <summary>
    /// Consecutive failed credential logins. Reset on a successful sign-in and when a lockout expires.
    /// </summary>
    /// <remarks>
    /// Per-IP throttling alone does not stop credential stuffing — an attacker with a pool of addresses
    /// spends one attempt per address. The count that has to hold is the one on the account itself.
    /// </remarks>
    public int FailedLoginCount { get; private set; }

    /// <summary>When set and still in the future, credential login is refused regardless of the password.</summary>
    public DateTime? LockedUntilUtc { get; private set; }

    /// <summary>
    /// Changes whenever the account's security context changes — ban, unban, deactivation, password
    /// change. Access tokens carry the stamp they were issued under and are rejected once it moves, so
    /// a ban takes effect immediately instead of at the end of the token's lifetime.
    /// </summary>
    public string SecurityStamp { get; private set; } = NewSecurityStamp();

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

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
    /// Creates a credential-login service account (Admin/Realtor/SuperAdmin) with a username and a
    /// pre-computed password hash. No phone number — these sign in with username/password, not OTP.
    /// The account is looked up at login by <paramref name="username"/>. Pass <paramref name="id"/> to
    /// pin a specific id (e.g. to match a hand-seeded row); when null a fresh id is generated.
    /// </summary>
    public static User CreateServiceAccount(string username, Role role, string passwordHash, Guid? id = null)
    {
        if (role is not (Role.Admin or Role.Realtor or Role.SuperAdmin))
            throw new DomainException("Service accounts must be Admin, Realtor or SuperAdmin.");
        if (string.IsNullOrWhiteSpace(username))
            throw new DomainException("Service account username is required.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Service account password hash is required.");

        return new User
        {
            Id = id ?? Guid.NewGuid(),
            Username = username,
            Role = role,
            IsActive = true,
            PasswordHash = passwordHash
        };
    }

    public void MarkPhoneVerified() => IsPhoneVerified = true;

    public void Deactivate()
    {
        IsActive = false;
        SecurityStamp = NewSecurityStamp();
    }

    // ── Credential-login lockout ─────────────────────────────────────────────

    /// <summary>True while an active lockout is in force.</summary>
    public bool IsLockedOut(DateTime utcNow) => LockedUntilUtc is { } until && until > utcNow;

    /// <summary>
    /// Records a failed password attempt and locks the account once <paramref name="maxAttempts"/> is
    /// reached. The counter is cleared alongside the lock so a lifted lockout starts from zero.
    /// </summary>
    public void RegisterFailedLogin(DateTime utcNow, int maxAttempts, TimeSpan lockoutFor)
    {
        // A lapsed lockout means the previous streak is spent; start counting again from this attempt.
        if (LockedUntilUtc is { } until && until <= utcNow)
        {
            LockedUntilUtc = null;
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
        {
            LockedUntilUtc = utcNow.Add(lockoutFor);
            FailedLoginCount = 0;
        }
    }

    /// <summary>True when a successful sign-in would actually have something to clear.</summary>
    /// <remarks>
    /// Callers check this before writing. A successful login is by far the common case, and issuing
    /// an UPDATE against the account row on every one of them buys nothing while adding a write —
    /// and a chance of two concurrent sign-ins colliding on the same row — to a path that otherwise
    /// only reads.
    /// </remarks>
    public bool HasLoginFailureState => FailedLoginCount != 0 || LockedUntilUtc is not null;

    /// <summary>Clears the failure streak and any lapsed lock after a successful sign-in.</summary>
    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockedUntilUtc = null;
    }

    /// <summary>
    /// Bans the account (idempotent). A banned account cannot obtain a token. An optional
    /// <paramref name="reason"/> is stored and shown to the user on the blocked screen; a blank
    /// reason is normalised to null.
    /// </summary>
    public void Ban(string? reason = null)
    {
        IsBanned = true;
        BanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        // Invalidate tokens already in the banned user's hands rather than waiting out their lifetime.
        SecurityStamp = NewSecurityStamp();
    }

    /// <summary>Lifts a ban (idempotent) and clears the stored ban reason.</summary>
    public void Unban()
    {
        IsBanned = false;
        BanReason = null;
        SecurityStamp = NewSecurityStamp();
    }

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
        // A password change ends every session issued under the old one.
        SecurityStamp = NewSecurityStamp();
        RegisterSuccessfulLogin();
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
        SecurityStamp = NewSecurityStamp();
    }
}
