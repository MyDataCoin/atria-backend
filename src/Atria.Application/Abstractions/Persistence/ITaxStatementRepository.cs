using Atria.Domain.Tax;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="TaxStatement"/>, the issued income statements.</summary>
public interface ITaxStatementRepository : IRepository<TaxStatement>
{
    /// <summary>The statement already issued to this investor for the year, or null.</summary>
    Task<TaxStatement?> FindAsync(Guid investorId, int year, CancellationToken ct);

    /// <summary>An investor's statements, newest year first.</summary>
    Task<IReadOnlyList<TaxStatement>> ListByInvestorAsync(Guid investorId, CancellationToken ct);

    /// <summary>
    /// Looks a statement up by the code printed on it. The lookup is by code alone, so possession of
    /// the document is what grants the check — there is nothing to enumerate.
    /// </summary>
    Task<TaxStatement?> FindByVerificationCodeAsync(string verificationCode, CancellationToken ct);
}
