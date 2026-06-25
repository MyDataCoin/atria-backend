using Atria.Domain.Investments;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="Property"/>.</summary>
public interface IPropertyRepository : IRepository<Property>
{
    Task<IReadOnlyList<Property>> GetAllAsync(CancellationToken ct);
}
