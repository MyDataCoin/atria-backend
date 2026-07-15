using Atria.Domain.Kyc;

namespace Atria.Application.Users.Dtos;

/// <summary>
/// Admin/Compliance overview row: a user joined with their (optional) KYC profile.
/// FullName is decrypted server-side at read time; KYC fields are null when the user
/// has no profile yet.
/// </summary>
/// <param name="Id">The user's unique identifier.</param>
/// <param name="PhoneNumber">The user's phone number (login identifier).</param>
/// <param name="FullName">Verified KYC full name (decrypted); <c>null</c> when unset or no profile.</param>
/// <param name="WalletAddress">Linked wallet address; <c>null</c> when unset or no profile.</param>
/// <param name="Status">KYC lifecycle status; <c>null</c> when the user has no KYC profile.</param>
/// <param name="Blocked">Whether the account is banned by a super admin (<c>status: Blocked</c> on the client).</param>
/// <param name="CreatedAtUtc">UTC timestamp when the user account was created.</param>
public sealed record UserOverviewDto(
    Guid Id,
    string? PhoneNumber,
    string? FullName,
    string? WalletAddress,
    KycStatus? Status,
    bool Blocked,
    DateTime CreatedAtUtc);
