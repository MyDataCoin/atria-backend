using Atria.Domain.Common;

namespace Atria.Domain.Operations;

/// <summary>
/// One line of an operating period's breakdown — "Аренда 1-го этажа — 420 000". Child entity of the
/// <see cref="OperatingPeriod"/> aggregate.
/// </summary>
/// <remarks>
/// A free-text label plus an amount rather than fixed columns, for the same reason the room breakdown
/// is: the same list has to describe rent from three tenants, a utility bill, and a one-off repair,
/// and no fixed set of columns survives contact with the second object.
/// </remarks>
public sealed class OperatingPeriodLine : Entity
{
    public Guid OperatingPeriodId { get; private set; }

    /// <summary>Which side of the period the line belongs to.</summary>
    public OperatingLineKind Kind { get; private set; }

    /// <summary>The line's label as it was entered.</summary>
    public string Label { get; private set; } = null!;

    /// <summary>The line's amount, always positive — <see cref="Kind"/> carries the sign.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Position in the list, so the breakdown displays in the order it was entered.</summary>
    public int SortOrder { get; private set; }

    private OperatingPeriodLine() { }

    internal static OperatingPeriodLine Create(
        Guid operatingPeriodId, OperatingLineKind kind, string label, decimal amount, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new DomainException("A line label is required.");

        var trimmed = label.Trim();
        if (trimmed.Length > OperatingPeriod.MaxLineLabel)
            throw new DomainException(
                $"A line label cannot exceed {OperatingPeriod.MaxLineLabel} characters.");

        // Positive only: the kind says whether it is money in or money out, so a negative expense
        // would be a second, contradictory way of saying "revenue" and the totals would depend on
        // which convention whoever typed it had in mind.
        if (amount <= 0)
            throw new DomainException("A line amount must be positive.");
        if (decimal.Round(amount, OperatingPeriod.MinorUnitScale) != amount)
            throw new DomainException(
                $"A line amount must be given to {OperatingPeriod.MinorUnitScale} decimal places.");

        return new OperatingPeriodLine
        {
            Id = Guid.NewGuid(),
            OperatingPeriodId = operatingPeriodId,
            Kind = kind,
            Label = trimmed,
            Amount = amount,
            SortOrder = sortOrder
        };
    }
}
