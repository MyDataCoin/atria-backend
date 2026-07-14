namespace Atria.Domain.Audit;

/// <summary>
/// How critical an audited action is. Drives the severity filter in the admin journal.
/// Wire values are lowercase.
/// </summary>
public enum AuditSeverity
{
    /// <summary>Routine, successful action (the default).</summary>
    Success = 0,

    /// <summary>Needs attention but is not a failure (e.g. an inbound support ticket).</summary>
    Warning = 1,

    /// <summary>Something went wrong or is security-relevant.</summary>
    Alert = 2
}
