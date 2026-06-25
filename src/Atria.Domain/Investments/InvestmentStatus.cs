namespace Atria.Domain.Investments;

/// <summary>Investment lifecycle. Moves to Active only after a confirmed payment.</summary>
public enum InvestmentStatus
{
    PendingPayment = 0,
    Active = 1,
    Failed = 2,
    Cancelled = 3
}
