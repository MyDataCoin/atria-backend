namespace Atria.Domain.Governance;

/// <summary>Lifecycle of a request for a second approval.</summary>
public enum CriticalActionStatus
{
    /// <summary>Requested; waiting for a second person to decide.</summary>
    Pending = 0,

    /// <summary>A second person approved it and the action was carried out.</summary>
    Approved = 1,

    /// <summary>A second person declined it.</summary>
    Rejected = 2,

    /// <summary>The requester withdrew it before anyone decided.</summary>
    Withdrawn = 3,

    /// <summary>Nobody decided within the approval window. Must be requested again.</summary>
    Expired = 4
}
