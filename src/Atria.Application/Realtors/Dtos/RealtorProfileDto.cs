namespace Atria.Application.Realtors.Dtos;

/// <summary>Read model of a realtor's business profile.</summary>
/// <param name="Id">The profile's unique identifier.</param>
/// <param name="UserId">The linked user id (role Realtor).</param>
/// <param name="FullName">Full name as a single line.</param>
/// <param name="Position">Position/title within the company; <c>null</c> when unset.</param>
/// <param name="WalletAddress">Crypto wallet address for payouts; <c>null</c> when unset.</param>
/// <param name="CompanyName">Registered legal entity name; <c>null</c> when unset.</param>
/// <param name="CompanyRegistrationNumber">Company registration number; <c>null</c> when unset.</param>
/// <param name="OfficeAddress">Legal / office address; <c>null</c> when unset.</param>
public sealed record RealtorProfileDto(
    Guid Id,
    Guid UserId,
    string FullName,
    string? Position,
    string? WalletAddress,
    string? CompanyName,
    string? CompanyRegistrationNumber,
    string? OfficeAddress);
