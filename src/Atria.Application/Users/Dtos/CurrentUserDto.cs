namespace Atria.Application.Users.Dtos;

/// <summary>
/// Who the caller is, as the panels need it: the display name for the header, and whether the
/// account is still sitting on a one-time password it has to replace.
/// </summary>
/// <remarks>
/// This is a lookup, not a token claim, on purpose: the access token is held client-side, so every
/// claim in it is published to whoever gets the token — the name stays server-side and is fetched
/// by the authenticated caller instead.
/// </remarks>
/// <param name="Id">The <c>users.id</c> of the caller.</param>
/// <param name="Role">The caller's role, e.g. <c>Admin</c>.</param>
/// <param name="Username">Login name for credential accounts; null for phone-OTP investors.</param>
/// <param name="FullName">Full name for staff accounts; null when none was recorded.</param>
/// <param name="MustChangePassword">
/// True while the account must replace its password before it can work — set when a super admin
/// creates the account or resets its password, cleared by <c>POST /auth/password/change</c>.
/// </param>
public sealed record CurrentUserDto(
    Guid Id,
    string Role,
    string? Username,
    string? FullName,
    bool MustChangePassword);
