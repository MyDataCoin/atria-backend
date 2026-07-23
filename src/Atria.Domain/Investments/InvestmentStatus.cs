namespace Atria.Domain.Investments;

/// <summary>
/// Investment (offering application) lifecycle. There is no payment on the platform: the investor
/// submits an application, which reserves tokens from the pool, and an operator approves it to make
/// it Active. A rejected/cancelled application returns its reserved tokens to the pool.
/// </summary>
public enum InvestmentStatus
{
    /// <summary>Application submitted; tokens are held from the pool, awaiting operator approval.</summary>
    Reserved = 0,

    /// <summary>Approved: tokens allocated to the investor (and, once chain wiring is on, minted).</summary>
    Active = 1,

    /// <summary>Operator declined the application; the reserved tokens were returned to the pool.</summary>
    Rejected = 2,

    /// <summary>Investor cancelled before approval; the reserved tokens were returned to the pool.</summary>
    Cancelled = 3,

    /// <summary>
    /// The reservation window lapsed without operator approval; the reserved tokens were returned to
    /// the pool automatically. Distinct from <see cref="Cancelled"/> (investor-initiated) so reporting
    /// can tell a lapsed reservation from an actively withdrawn one.
    /// </summary>
    Expired = 4
}
