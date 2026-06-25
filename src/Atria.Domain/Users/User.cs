using Atria.Domain.Common;

namespace Atria.Domain.Users;

/// <summary>
/// Account aggregate. Created either with email + password (any role) or from a
/// verified phone number (Investor). Verification flags and soft-delete are driven
/// by explicit domain operations; timestamps come from the persistence layer.
/// </summary>
public sealed class User : AggregateRoot
{
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? PasswordHash { get; private set; }
    public Role Role { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public bool IsPhoneVerified { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    // private ctor: creation only through the static factory methods
    private User() { }

    /// <summary>Creates an email/password account for the given role.</summary>
    public static User CreateWithPassword(
        string email, string passwordHash, Role role, string? firstName, string? lastName)
        => new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHash,
            Role = role,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true
        };

    /// <summary>Creates an Investor account from a (verified) phone number.</summary>
    public static User CreateFromPhone(string phoneNumber, Role role)
        => new()
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            Role = role,
            IsActive = true
        };

    public void MarkEmailVerified() => IsEmailVerified = true;

    public void MarkPhoneVerified() => IsPhoneVerified = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Soft-deletes the account: records the timestamp and deactivates it.</summary>
    public void SoftDelete(DateTime utc)
    {
        DeletedAtUtc = utc;
        IsActive = false;
    }
}
