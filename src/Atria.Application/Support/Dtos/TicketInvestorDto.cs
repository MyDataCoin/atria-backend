using DomainRole = Atria.Domain.Users.Role;

namespace Atria.Application.Support.Dtos;

/// <summary>
/// Who opened a ticket. Present on Admin list/detail so the panel can show the author;
/// omitted (null) for a client viewing their own tickets.
/// </summary>
/// <param name="Id">The author's user id.</param>
/// <param name="FullName">The investor's verified KYC full name (decrypted); <c>null</c> when unset, no profile, or the requester is a realtor (anonymous in the UI).</param>
/// <param name="Role">The author's role, lowercase: <c>investor</c> | <c>realtor</c>, so the panel can tell a realtor ticket from an investor one.</param>
public sealed record TicketInvestorDto(Guid Id, string? FullName, string Role)
{
    /// <summary>Maps the author's domain role to its lowercase wire value (defaults to <c>investor</c>).</summary>
    public static string ToWireRole(DomainRole role) => role == DomainRole.Realtor ? "realtor" : "investor";

    /// <summary>
    /// Builds the admin-panel author block: the KYC full name for an investor, or no personal name
    /// for a realtor (anonymous in the UI — only the role identifies them).
    /// </summary>
    public static TicketInvestorDto ForAdmin(Guid authorId, string? kycFullName, DomainRole authorRole)
    {
        var name = authorRole == DomainRole.Realtor ? null : kycFullName;
        return new TicketInvestorDto(authorId, name, ToWireRole(authorRole));
    }
}
