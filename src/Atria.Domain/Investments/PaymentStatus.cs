namespace Atria.Domain.Investments;

/// <summary>Status of a single payment transaction.</summary>
public enum PaymentStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}
