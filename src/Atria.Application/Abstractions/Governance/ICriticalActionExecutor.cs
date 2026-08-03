using Atria.Application.Common;
using Atria.Domain.Governance;

namespace Atria.Application.Abstractions;

/// <summary>
/// Carries out one kind of critical action once a second person has approved it. Approval and
/// execution are separate on purpose: the approval handler knows nothing about banning a user or
/// publishing an issue, and each executor knows nothing about who approved it.
/// </summary>
public interface ICriticalActionExecutor
{
    /// <summary>The kind this executor handles.</summary>
    CriticalActionKind Kind { get; }

    /// <summary>
    /// Performs the approved action. Runs inside the approving unit of work, so a failure here leaves
    /// the request pending rather than marking it approved with nothing carried out.
    /// </summary>
    Task<Result> ExecuteAsync(CriticalAction action, CancellationToken ct);
}
