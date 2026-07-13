namespace Atria.Domain.Users;

/// <summary>Authorization roles. Mapped to JWT role claims.</summary>
public enum Role
{
    Admin = 0,
    Investor = 1,
    Compliance = 2,
    Realtor = 3
}
