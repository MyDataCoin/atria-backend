namespace Atria.Application.SuperAdmin.Dtos;

/// <summary>
/// Result of a super-admin password reset: the plaintext password the super admin passes on to the
/// admin/realtor. Returned once and never stored in plaintext.
/// </summary>
/// <param name="TemporaryPassword">The new password (generated, or the explicit one that was set).</param>
public sealed record ResetPasswordResultDto(string TemporaryPassword);
