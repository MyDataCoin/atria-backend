namespace Atria.Domain.Operations;

/// <summary>Where an operating period stands.</summary>
public enum OperatingPeriodStatus
{
    /// <summary>Being filled in. The figures can still change and nothing may be distributed from it.</summary>
    Draft = 0,

    /// <summary>
    /// Confirmed by the owner's side. The figures are frozen and a distribution may be declared
    /// against the period.
    /// </summary>
    Confirmed = 1,

    /// <summary>A distribution has been declared from this period.</summary>
    Distributed = 2
}
