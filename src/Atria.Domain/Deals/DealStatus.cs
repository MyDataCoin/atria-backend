namespace Atria.Domain.Deals;

/// <summary>
/// Referral deal lifecycle. A deal is <see cref="Pending"/> while its referral link is live,
/// becomes <see cref="Successful"/> when an investor buys through the link, or <see cref="Rejected"/>
/// when the link expires unused.
/// </summary>
public enum DealStatus
{
    Pending = 0,
    Successful = 1,
    Rejected = 2
}
