namespace Atria.Domain.Users;

/// <summary>Authorization roles. Mapped to JWT role claims.</summary>
public enum Role
{
    Admin = 0,
    Investor = 1,
    Compliance = 2,
    Realtor = 3,

    /// <summary>
    /// Platform super administrator: bans accounts and resets admin/realtor passwords. Its name
    /// deliberately contains "super" — the frontend routes any role matching <c>*super*</c> to the
    /// super-admin app, and the JWT carries the role as its literal <c>ToString()</c>.
    /// </summary>
    SuperAdmin = 4
}
