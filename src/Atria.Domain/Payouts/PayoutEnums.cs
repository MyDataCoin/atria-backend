namespace Atria.Domain.Payouts;

/// <summary>Where a distribution stands.</summary>
public enum PayoutRunStatus
{
    /// <summary>Computed against a frozen register and waiting for a second approval.</summary>
    Draft = 0,

    /// <summary>Approved by two people; the money may now be sent and references recorded.</summary>
    Approved = 1,

    /// <summary>Every line has been settled or written off as failed.</summary>
    Completed = 2,

    /// <summary>Called off before any money moved.</summary>
    Cancelled = 3
}

/// <summary>Where one holder's line stands.</summary>
public enum PayoutItemStatus
{
    /// <summary>Owed and not yet paid.</summary>
    Pending = 0,

    /// <summary>Paid, with a reference proving it.</summary>
    Paid = 1,

    /// <summary>The payment could not be delivered. Still owed — the reason says what to fix.</summary>
    Failed = 2
}

/// <summary>
/// How the money physically reaches the holder.
/// </summary>
/// <remarks>
/// Recorded per run rather than fixed platform-wide, because it is genuinely a per-distribution
/// question: the same issue may pay one distribution by bank transfer and the next in stablecoin.
/// Either way the platform does not move the funds — it computes what is owed, holds the approval,
/// and records the reference the transfer came back with.
/// </remarks>
public enum PayoutMethod
{
    /// <summary>Bank transfer to the holder's account. The reference is the payment order number.</summary>
    BankTransfer = 0,

    /// <summary>Transfer to the holder's wallet address. The reference is the transaction hash.</summary>
    Wallet = 1
}

/// <summary>What is being distributed.</summary>
public enum PayoutKind
{
    /// <summary>Income distributed to holders in proportion to their shares.</summary>
    Dividend = 0,

    /// <summary>Return of capital on an issue being wound up.</summary>
    CapitalReturn = 1
}
