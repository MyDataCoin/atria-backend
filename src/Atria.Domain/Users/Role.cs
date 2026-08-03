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
    SuperAdmin = 4,

    /// <summary>
    /// Computes and runs investor distributions. Separated from <see cref="Admin"/> so that moving
    /// money is not something the operator who approves applications can also do alone.
    /// </summary>
    Finance = 5,

    /// <summary>
    /// Responsible for the collateral behind an issue. Deliberately narrow: encumbrance status and the
    /// holder register, nothing else — it is an oversight role, not an operating one.
    /// </summary>
    CollateralManager = 6,

    /// <summary>Read-only across the platform. Sees everything, changes nothing.</summary>
    Auditor = 7
}
