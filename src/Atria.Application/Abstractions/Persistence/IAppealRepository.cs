using Atria.Domain.Appeals;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for ban <see cref="Appeal"/>s.</summary>
public interface IAppealRepository : IRepository<Appeal>
{
    /// <summary>
    /// All appeals, newest first, each with the appellant's full name resolved best-effort from the
    /// realtor profile of the account matching the appeal's username (null when no match). SuperAdmin
    /// reporting read.
    /// </summary>
    Task<IReadOnlyList<(Appeal Appeal, string? FullName)>> GetAllWithNamesAsync(CancellationToken ct);
}
