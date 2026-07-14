using Atria.Domain.Common;

namespace Atria.Domain.Realtors;

/// <summary>
/// Business profile of a realtor, linked one-to-one to their <c>users</c> row (role Realtor).
/// Holds contact/company details used on the realtor dashboard and in deal paperwork. The phone
/// number is NOT stored here — it lives on the linked user. All fields are plain text (no PII
/// encryption); populated by developers via SQL for now.
/// </summary>
public sealed class RealtorProfile : AggregateRoot
{
    /// <summary>The linked user's id (role Realtor). Unique — one profile per user.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Full name as a single line.</summary>
    public string FullName { get; private set; } = null!;

    /// <summary>Position/title within the company.</summary>
    public string? Position { get; private set; }

    /// <summary>Crypto wallet address for payouts.</summary>
    public string? WalletAddress { get; private set; }

    /// <summary>Registered legal entity name.</summary>
    public string? CompanyName { get; private set; }

    /// <summary>Company registration number.</summary>
    public string? CompanyRegistrationNumber { get; private set; }

    /// <summary>Legal / office address.</summary>
    public string? OfficeAddress { get; private set; }

    // private ctor: creation only through the factory method
    private RealtorProfile() { }

    /// <summary>Creates a realtor profile for a user. Only <paramref name="fullName"/> is required.</summary>
    public static RealtorProfile Create(
        Guid userId,
        string fullName,
        string? position = null,
        string? walletAddress = null,
        string? companyName = null,
        string? companyRegistrationNumber = null,
        string? officeAddress = null)
    {
        if (userId == Guid.Empty)
            throw new DomainException("User is required for a realtor profile.");
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Realtor full name is required.");

        return new RealtorProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FullName = fullName,
            Position = position,
            WalletAddress = walletAddress,
            CompanyName = companyName,
            CompanyRegistrationNumber = companyRegistrationNumber,
            OfficeAddress = officeAddress
        };
    }
}
