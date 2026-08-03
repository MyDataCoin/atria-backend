using System.Security.Cryptography;
using Atria.Domain.Common;

namespace Atria.Domain.Tax;

/// <summary>
/// An income statement issued to an investor for a calendar year — the document they hand to the tax
/// office.
///
/// Issued and stored by the server, never assembled in the browser. A document produced client-side
/// carries no evidence of anything: its number is whatever the page invented, and nobody can check
/// afterwards that the figures on the paper are the figures the platform holds. Here the statement is
/// a record with its own number and verification code, and what was issued stays exactly as issued.
/// </summary>
public sealed class TaxStatement : AggregateRoot
{
    /// <summary>The investor the statement is issued to.</summary>
    public Guid InvestorId { get; private set; }

    /// <summary>Calendar year the statement covers.</summary>
    public int Year { get; private set; }

    /// <summary>Human-readable document number, printed on the statement.</summary>
    public string Number { get; private set; } = null!;

    /// <summary>
    /// Unguessable code the statement is verified by. Whoever holds the document can check it; nobody
    /// can enumerate other people's statements from the outside.
    /// </summary>
    public string VerificationCode { get; private set; } = null!;

    /// <summary>Investor's name as it stood in KYC when the statement was issued.</summary>
    public string InvestorFullName { get; private set; } = null!;

    /// <summary>Total invested across the investor's holdings at issue time.</summary>
    public decimal TotalInvested { get; private set; }

    /// <summary>
    /// Income paid out over the year. Zero while no distribution has ever been made — the statement
    /// says so plainly rather than leaving the reader to guess.
    /// </summary>
    public decimal TotalIncome { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>The per-issue breakdown behind the totals, as JSON.</summary>
    public string Content { get; private set; } = null!;

    public DateTime IssuedAtUtc { get; private set; }

    private TaxStatement() { }

    public static TaxStatement Issue(
        Guid investorId, int year, string investorFullName, decimal totalInvested, decimal totalIncome,
        string currency, string content, DateTime issuedAtUtc)
    {
        if (investorId == Guid.Empty)
            throw new DomainException("InvestorId is required.");
        if (year < 2000 || year > 2200)
            throw new DomainException("Year is out of range.");
        if (string.IsNullOrWhiteSpace(investorFullName))
            throw new DomainException("A verified name is required to issue a statement.");
        if (totalInvested < 0 || totalIncome < 0)
            throw new DomainException("Statement amounts cannot be negative.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException("Statement content is required.");

        var code = NewVerificationCode();

        return new TaxStatement
        {
            Id = Guid.NewGuid(),
            InvestorId = investorId,
            Year = year,
            // The number is printed and quoted; the code is what actually proves anything.
            Number = $"ATRIA-{year}-{code[..6]}",
            VerificationCode = code,
            InvestorFullName = investorFullName,
            TotalInvested = totalInvested,
            TotalIncome = totalIncome,
            Currency = currency,
            Content = content,
            IssuedAtUtc = issuedAtUtc
        };
    }

    /// <summary>Cryptographically random, URL-safe, uppercase — readable over the phone if need be.</summary>
    private static string NewVerificationCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I/O/0/1: they get misread
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);

        return string.Create(bytes.Length, bytes.ToArray(), (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = alphabet[source[i] % alphabet.Length];
        });
    }
}
