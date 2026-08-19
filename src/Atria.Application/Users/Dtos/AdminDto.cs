namespace Atria.Application.Users.Dtos;

/// <summary>
/// A staff (credential-login) account for the super-admin "Admins" tab: an account that has a
/// password and can be password-reset. Email is not stored for staff accounts and is always null;
/// FullName is what the super admin entered when creating the account (null for accounts seeded
/// before it was recorded).
/// </summary>
/// <param name="Id">The <c>users.id</c> — password reset/restore and ban target this id.</param>
/// <param name="FullName">Display name entered when the account was created; may be <c>null</c>.</param>
/// <param name="Username">Login name.</param>
/// <param name="Email">Contact email; <c>null</c> (not stored for staff accounts).</param>
/// <param name="Blocked">Whether the account is banned by a super admin.</param>
public sealed record AdminDto(
    Guid Id,
    string? FullName,
    string? Username,
    string? Email,
    bool Blocked);
