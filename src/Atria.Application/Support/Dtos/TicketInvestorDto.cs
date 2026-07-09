namespace Atria.Application.Support.Dtos;

/// <summary>
/// Who opened a ticket. Present on Admin list/detail so the panel can show the investor;
/// omitted (null) for an investor viewing their own tickets.
/// </summary>
/// <param name="Id">The investor's user id.</param>
/// <param name="FullName">The investor's verified KYC full name (decrypted); <c>null</c> when unset or no profile.</param>
public sealed record TicketInvestorDto(Guid Id, string? FullName);
