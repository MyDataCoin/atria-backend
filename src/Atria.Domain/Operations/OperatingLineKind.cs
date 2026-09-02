namespace Atria.Domain.Operations;

/// <summary>Which side of the period a breakdown line belongs to.</summary>
public enum OperatingLineKind
{
    /// <summary>Money in: rent, parking, a one-off recovery.</summary>
    Revenue = 0,

    /// <summary>Money out: maintenance, utilities, management fee, tax.</summary>
    Expense = 1
}
