using Atria.Domain.Governance;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="CriticalAction"/>, the dual-approval record.</summary>
public interface ICriticalActionRepository : IRepository<CriticalAction>
{
    /// <summary>
    /// The request still awaiting a decision for this kind and target, or null. Stops a second request
    /// for the same thing from piling up while the first is open.
    /// </summary>
    Task<CriticalAction?> FindPendingAsync(CriticalActionKind kind, Guid targetId, CancellationToken ct);

    /// <summary>Requests awaiting a decision, oldest first.</summary>
    Task<IReadOnlyList<CriticalAction>> ListPendingAsync(CancellationToken ct);

    /// <summary>Recently decided requests, newest first — the audit view.</summary>
    Task<IReadOnlyList<CriticalAction>> ListDecidedAsync(int take, CancellationToken ct);
}
