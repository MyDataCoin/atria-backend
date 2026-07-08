namespace Atria.Application.Investments.Dtos;

/// <summary>
/// One investor's Active holding in a property: their verified KYC name (decrypted) and the
/// total tokens they hold (Σ amount / token price). Admin/Compliance view. The share percent is
/// computed on the client as tokens / totalTokens * 100.
/// </summary>
/// <param name="FullName">Investor's verified KYC full name (decrypted); <c>null</c> when unset or no profile.</param>
/// <param name="Tokens">Total tokens held across the investor's Active investments in the property.</param>
public sealed record PropertyInvestorDto(string? FullName, int Tokens);
