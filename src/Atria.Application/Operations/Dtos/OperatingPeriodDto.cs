using Atria.Domain.Operations;

namespace Atria.Application.Operations.Dtos;

/// <summary>One line of a period's breakdown.</summary>
/// <param name="Id">The line's identifier.</param>
/// <param name="Kind">Which side it belongs to: <c>revenue</c> | <c>expense</c>.</param>
/// <param name="Label">The line's label as it was entered.</param>
/// <param name="Amount">The amount, always positive — <paramref name="Kind"/> carries the sign.</param>
public sealed record OperatingPeriodLineDto(Guid Id, string Kind, string Label, decimal Amount);

/// <summary>What an issue earned and spent over one stretch of time.</summary>
/// <param name="Id">The period's identifier.</param>
/// <param name="PropertyId">The issue the period belongs to.</param>
/// <param name="StartUtc">First day of the period, inclusive.</param>
/// <param name="EndUtc">Last day of the period, inclusive.</param>
/// <param name="GrossRevenue">Rent and other income collected.</param>
/// <param name="OperatingExpenses">Costs incurred.</param>
/// <param name="NetIncome">Revenue less expenses. Negative on a loss-making period.</param>
/// <param name="Currency">Currency of the figures. Always <c>KGS</c>.</param>
/// <param name="Status">Where the period stands, lowercase: <c>draft</c> | <c>confirmed</c> | <c>distributed</c>.</param>
/// <param name="ReportedByUserId">Who reported the figures.</param>
/// <param name="ConfirmedByUserId">Who confirmed them; <c>null</c> while the period is a draft.</param>
/// <param name="ConfirmedAtUtc">When they were confirmed; <c>null</c> while the period is a draft.</param>
/// <param name="PayoutRunId">The distribution declared from this period; <c>null</c> until one is.</param>
/// <param name="Note">Free-text note from whoever reported the period.</param>
/// <param name="Lines">The breakdown, in the order it was entered.</param>
/// <param name="LinesRevenue">Sum of the revenue lines. Compare with <paramref name="GrossRevenue"/> to flag a breakdown that does not add up — the server does not reject a mismatch.</param>
/// <param name="LinesExpenses">Sum of the expense lines.</param>
public sealed record OperatingPeriodDto(
    Guid Id,
    Guid PropertyId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal GrossRevenue,
    decimal OperatingExpenses,
    decimal NetIncome,
    string Currency,
    string Status,
    Guid ReportedByUserId,
    Guid? ConfirmedByUserId,
    DateTime? ConfirmedAtUtc,
    Guid? PayoutRunId,
    string? Note,
    IReadOnlyList<OperatingPeriodLineDto> Lines,
    decimal LinesRevenue,
    decimal LinesExpenses)
{
    /// <summary>Maps a period aggregate to its read model.</summary>
    public static OperatingPeriodDto From(OperatingPeriod p)
    {
        var lines = p.Lines
            .OrderBy(l => l.SortOrder)
            .Select(l => new OperatingPeriodLineDto(l.Id, ToWireKind(l.Kind), l.Label, l.Amount))
            .ToList();

        return new OperatingPeriodDto(
            p.Id, p.PropertyId, p.StartUtc, p.EndUtc, p.GrossRevenue, p.OperatingExpenses,
            p.NetIncome, p.Currency, ToWireStatus(p.Status), p.ReportedByUserId,
            p.ConfirmedByUserId, p.ConfirmedAtUtc, p.PayoutRunId, p.Note, lines,
            p.Lines.Where(l => l.Kind == OperatingLineKind.Revenue).Sum(l => l.Amount),
            p.Lines.Where(l => l.Kind == OperatingLineKind.Expense).Sum(l => l.Amount));
    }

    /// <summary>Maps the status to its lowercase wire value.</summary>
    public static string ToWireStatus(OperatingPeriodStatus status) => status switch
    {
        OperatingPeriodStatus.Confirmed => "confirmed",
        OperatingPeriodStatus.Distributed => "distributed",
        _ => "draft"
    };

    /// <summary>Maps a line kind to its lowercase wire value.</summary>
    public static string ToWireKind(OperatingLineKind kind)
        => kind == OperatingLineKind.Expense ? "expense" : "revenue";

    /// <summary>
    /// Parses a wire line kind. Anything unrecognised is a revenue line, which is the safer default:
    /// an unknown value inflating income is visible against the total, where one silently becoming an
    /// expense would quietly shrink what is distributable.
    /// </summary>
    public static OperatingLineKind ParseKind(string? wire)
        => wire?.Trim().ToLowerInvariant() == "expense"
            ? OperatingLineKind.Expense
            : OperatingLineKind.Revenue;
}

/// <summary>One line of a reported breakdown, as it arrives from a client.</summary>
/// <param name="Kind"><c>revenue</c> | <c>expense</c>.</param>
/// <param name="Label">The line's label.</param>
/// <param name="Amount">The amount; must be positive.</param>
public sealed record OperatingPeriodLineInput(string Kind, string Label, decimal Amount);
