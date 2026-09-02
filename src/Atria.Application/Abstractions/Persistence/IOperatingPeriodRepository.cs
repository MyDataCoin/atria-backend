using Atria.Domain.Operations;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="OperatingPeriod"/> — what an issue earned and spent.</summary>
public interface IOperatingPeriodRepository : IRepository<OperatingPeriod>
{
    /// <summary>An issue's periods with their breakdown, most recent period first.</summary>
    Task<IReadOnlyList<OperatingPeriod>> ListByPropertyAsync(Guid propertyId, CancellationToken ct);

    /// <summary>
    /// The period covering exactly <paramref name="startUtc"/>–<paramref name="endUtc"/>, if one has
    /// been reported. Used to refuse a duplicate before the unique index does, so the caller gets a
    /// conflict it can explain rather than a database error.
    /// </summary>
    Task<OperatingPeriod?> FindByRangeAsync(
        Guid propertyId, DateTime startUtc, DateTime endUtc, CancellationToken ct);
}
