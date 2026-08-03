namespace Atria.Domain.Governance;

/// <summary>
/// Actions no single person may carry out alone. Each one either moves money, cuts an investor off
/// from their holdings, or puts an issue in front of the public — the three places where one
/// compromised or mistaken account does damage that cannot be quietly undone.
/// </summary>
public enum CriticalActionKind
{
    /// <summary>Starting a distribution to investors. Target: the payout run.</summary>
    PayoutRun = 0,

    /// <summary>Blocking an investor's account. Target: the user.</summary>
    InvestorBlock = 1,

    /// <summary>Publishing an issue, opening it to investors. Target: the property.</summary>
    IssuePublication = 2
}
