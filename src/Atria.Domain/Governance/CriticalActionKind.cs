namespace Atria.Domain.Governance;

/// <summary>
/// Actions no single person may carry out alone. Each one either moves money or cuts an investor off
/// from their holdings — the places where one compromised or mistaken account does damage that
/// cannot be quietly undone.
/// </summary>
/// <remarks>
/// Publishing an issue is deliberately NOT one of them: opening an offering is an ordinary editorial
/// act an administrator performs on their own, and it is reversible (unannounce / complete).
/// </remarks>
public enum CriticalActionKind
{
    /// <summary>Starting a distribution to investors. Target: the payout run.</summary>
    PayoutRun = 0,

    /// <summary>Blocking an investor's account. Target: the user.</summary>
    InvestorBlock = 1
}
