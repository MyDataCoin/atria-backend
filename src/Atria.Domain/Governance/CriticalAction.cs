using Atria.Domain.Common;

namespace Atria.Domain.Governance;

/// <summary>
/// A request for a second pair of eyes on an action that must never be taken alone: starting a payout,
/// blocking an investor, publishing an issue. One person requests it, a different person decides, and
/// only then is the action carried out.
///
/// The record survives the decision: who asked, who approved, when, and why is exactly what an auditor
/// or a regulator asks for after the fact.
/// </summary>
public sealed class CriticalAction : AggregateRoot
{
    /// <summary>How long a request waits for a decision before it has to be raised again.</summary>
    public static readonly TimeSpan ApprovalWindow = TimeSpan.FromHours(24);

    /// <summary>What is being asked for.</summary>
    public CriticalActionKind Kind { get; private set; }

    /// <summary>The entity the action applies to — the user to block, the property to publish.</summary>
    public Guid TargetId { get; private set; }

    /// <summary>Why the requester is asking. Carried into the executed action where it has a meaning.</summary>
    public string? Reason { get; private set; }

    /// <summary>Who asked.</summary>
    public Guid RequestedByUserId { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>After this instant the request can no longer be approved.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    public CriticalActionStatus Status { get; private set; }

    /// <summary>Who decided. Never the same person as <see cref="RequestedByUserId"/>.</summary>
    public Guid? DecidedByUserId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>The decider's note — required when declining, so a refusal is never unexplained.</summary>
    public string? DecisionNote { get; private set; }

    private CriticalAction() { }

    /// <summary>Raises a request for approval, open for <see cref="ApprovalWindow"/>.</summary>
    public static CriticalAction Request(
        CriticalActionKind kind, Guid targetId, string? reason, Guid requestedByUserId, DateTime nowUtc)
    {
        if (targetId == Guid.Empty)
            throw new DomainException("Target is required.");
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("Requester is required.");

        return new CriticalAction
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            TargetId = targetId,
            Reason = reason,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(ApprovalWindow),
            Status = CriticalActionStatus.Pending
        };
    }

    /// <summary>True once the window has closed on a request nobody decided.</summary>
    public bool IsExpiredAt(DateTime nowUtc)
        => Status == CriticalActionStatus.Pending && nowUtc >= ExpiresAtUtc;

    /// <summary>
    /// Records a second person's approval. The approver must be someone other than the requester —
    /// that separation is the entire point, so it is enforced here rather than left to a caller.
    /// </summary>
    public void Approve(Guid approverUserId, DateTime nowUtc)
    {
        EnsureDecidable(approverUserId, nowUtc);

        Status = CriticalActionStatus.Approved;
        DecidedByUserId = approverUserId;
        DecidedAtUtc = nowUtc;
    }

    /// <summary>Records a second person's refusal, with the reason for it.</summary>
    public void Reject(Guid approverUserId, string note, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("A reason is required to decline a critical action.");

        EnsureDecidable(approverUserId, nowUtc);

        Status = CriticalActionStatus.Rejected;
        DecidedByUserId = approverUserId;
        DecidedAtUtc = nowUtc;
        DecisionNote = note;
    }

    /// <summary>The requester withdraws their own request before anyone has decided.</summary>
    public void Withdraw(Guid userId, DateTime nowUtc)
    {
        if (Status != CriticalActionStatus.Pending)
            throw new DomainException("Only a pending request can be withdrawn.");
        if (userId != RequestedByUserId)
            throw new DomainException("Only the requester can withdraw their request.");

        Status = CriticalActionStatus.Withdrawn;
        DecidedAtUtc = nowUtc;
    }

    /// <summary>Closes a request nobody decided in time.</summary>
    public void Expire(DateTime nowUtc)
    {
        if (Status != CriticalActionStatus.Pending)
            throw new DomainException("Only a pending request can expire.");

        Status = CriticalActionStatus.Expired;
        DecidedAtUtc = nowUtc;
    }

    private void EnsureDecidable(Guid approverUserId, DateTime nowUtc)
    {
        if (Status != CriticalActionStatus.Pending)
            throw new DomainException("This request has already been decided.");
        if (approverUserId == Guid.Empty)
            throw new DomainException("Approver is required.");
        if (approverUserId == RequestedByUserId)
            throw new DomainException("A critical action cannot be approved by the person who requested it.");
        if (nowUtc >= ExpiresAtUtc)
            throw new DomainException("The approval window for this request has closed.");
    }
}
