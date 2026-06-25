using Atria.Domain.Common;

namespace Atria.Domain.Users;

/// <summary>
/// Account aggregate. Authentication is phone-only: accounts are created from a verified
/// Kyrgyzstan phone number (Investor). Email is optional CONTACT info (e.g. for receipts),
/// never a login credential. Verification flags and soft-delete are driven by explicit
/// domain operations; timestamps come from the persistence layer.
/// </summary>
public sealed class User : AggregateRoot
{
    public string? PhoneNumber { get; private set; }
    public Role Role { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    /// <summary>Optional contact email (not used for authentication).</summary>
    public string? Email { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public bool IsPhoneVerified { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

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

    /// <summary>Sets/updates the optional contact email and resets its verified flag.</summary>
    public void SetEmail(string email)
    {
        Email = email;
        IsEmailVerified = false;
    }

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
