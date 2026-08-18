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
    Expired = 4,

    /// <summary>
    /// The holder exercised the 14-day right of withdrawal (draft Decree, §44): the shares are
    /// withdrawn from them and the money is owed back. Distinct from <see cref="Cancelled"/>, which
    /// happens before approval and involves no shares and no refund.
    /// </summary>
    Withdrawn = 5,

    /// <summary>
    /// An operator voided the application because it should not stand — a mistaken entry, a duplicate,
    /// or data left over from testing. The shares go back to the pool.
    /// </summary>
    /// <remarks>
    /// Kept apart from every other closing status on purpose: the platform's own correction is not the
    /// investor changing their mind (<see cref="Cancelled"/>), not a failed application
    /// (<see cref="Rejected"/>), and not the statutory right of withdrawal (<see cref="Withdrawn"/>).
    /// Placement figures and the audit trail have to be able to tell them apart.
    /// </remarks>
    Annulled = 6
}
