using Atria.Domain.Documents;

namespace Atria.Application.Abstractions;

/// <summary>
/// Specialized repository for <see cref="DocumentRecord"/> aggregates.
/// Adds an owner-scoped query for "my documents".
/// </summary>
public interface IDocumentRepository : IRepository<DocumentRecord>
{
    Task<IReadOnlyList<DocumentRecord>> GetByOwnerAsync(Guid ownerId, CancellationToken ct);
}
