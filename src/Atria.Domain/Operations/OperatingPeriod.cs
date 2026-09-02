using Atria.Domain.Common;

namespace Atria.Domain.Operations;

/// <summary>
/// What an issue actually earned and spent over one stretch of time: rent collected, costs incurred,
/// and the net that is available to distribute.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a distribution used to start from a number somebody typed. <c>PayoutRun</c>
/// takes a declared amount and divides it perfectly across the register — but nothing said where the
/// amount came from, and "how was this figure arrived at?" is the first question an audit asks and
/// the one the platform could not answer. A period is that answer: the distribution is declared from
/// a confirmed set of figures, and the two are linked.
/// </para>
/// <para>
/// The owner's side reports the figures and the owner's side confirms them — deliberately as two
/// steps, because the management company asked for exactly that separation ("расчёт и подтверждение
/// делают разные люди"). Nothing can be distributed from a period still being filled in.
/// </para>
/// </remarks>
public sealed class OperatingPeriod : AggregateRoot
{
    /// <summary>Fractional digits money is kept to. Every currency the platform deals in is a two-decimal currency.</summary>
    public const int MinorUnitScale = 2;

    /// <summary>Longest a line item's label may be.</summary>
    public const int MaxLineLabel = 200;

    public Guid PropertyId { get; private set; }

    /// <summary>First day of the period, inclusive (UTC, date only).</summary>
    public DateTime StartUtc { get; private set; }

    /// <summary>Last day of the period, inclusive (UTC, date only).</summary>
    public DateTime EndUtc { get; private set; }

    /// <summary>Rent and other income collected over the period.</summary>
    public decimal GrossRevenue { get; private set; }

    /// <summary>Costs incurred over the period — maintenance, utilities, management fees, tax.</summary>
    public decimal OperatingExpenses { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>
    /// What is left to distribute: revenue less expenses. Can be negative — a loss-making period is a
    /// fact to record, not an error to reject; it simply has nothing to distribute.
    /// </summary>
    public decimal NetIncome => GrossRevenue - OperatingExpenses;

    /// <summary>Whether the period has anything to distribute at all.</summary>
    public bool HasDistributableIncome => NetIncome > 0;

    public OperatingPeriodStatus Status { get; private set; }

    /// <summary>Who reported the figures.</summary>
    public Guid ReportedByUserId { get; private set; }

    /// <summary>
    /// Who confirmed them. Never the same person as <see cref="ReportedByUserId"/> — the whole point
    /// of confirming is that a second pair of eyes saw the numbers.
    /// </summary>
    public Guid? ConfirmedByUserId { get; private set; }

    public DateTime? ConfirmedAtUtc { get; private set; }

    /// <summary>The distribution declared from this period, once one has been.</summary>
    public Guid? PayoutRunId { get; private set; }

    /// <summary>Free-text note from whoever reported the period.</summary>
    public string? Note { get; private set; }

    private readonly List<OperatingPeriodLine> _lines = new();

    /// <summary>The breakdown behind the two totals, in the order it was entered.</summary>
    public IReadOnlyCollection<OperatingPeriodLine> Lines => _lines.AsReadOnly();

    private OperatingPeriod() { }

    /// <summary>Opens a period for an issue and records the figures reported for it.</summary>
    public static OperatingPeriod Report(
        Guid propertyId, DateTime startUtc, DateTime endUtc, decimal grossRevenue,
        decimal operatingExpenses, string currency, Guid reportedByUserId, string? note)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");
        if (reportedByUserId == Guid.Empty)
            throw new DomainException("ReportedByUserId is required.");
        if (endUtc < startUtc)
            throw new DomainException("A period cannot end before it starts.");
        if (grossRevenue < 0)
            throw new DomainException("Revenue cannot be negative.");
        if (operatingExpenses < 0)
            throw new DomainException("Expenses cannot be negative.");

        // Refused rather than rounded, for the same reason a declared distribution is: the figures
        // here become the ceiling on what may be paid out, and a third decimal would make the period
        // and the run disagree about what that ceiling is by a fraction nobody could account for.
        RequireMinorUnits(grossRevenue, "Revenue");
        RequireMinorUnits(operatingExpenses, "Expenses");

        var periodCurrency = Money.Require(currency);

        return new OperatingPeriod
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            StartUtc = startUtc.Date,
            EndUtc = endUtc.Date,
            GrossRevenue = grossRevenue,
            OperatingExpenses = operatingExpenses,
            Currency = periodCurrency,
            Status = OperatingPeriodStatus.Draft,
            ReportedByUserId = reportedByUserId,
            Note = note
        };
    }

    /// <summary>
    /// Revises the figures while the period is still a draft. Once confirmed they are frozen: a
    /// distribution may already have been sized against them.
    /// </summary>
    public void Revise(decimal grossRevenue, decimal operatingExpenses, string? note)
    {
        if (Status != OperatingPeriodStatus.Draft)
            throw new DomainException("Only a draft period can be revised.");
        if (grossRevenue < 0)
            throw new DomainException("Revenue cannot be negative.");
        if (operatingExpenses < 0)
            throw new DomainException("Expenses cannot be negative.");

        RequireMinorUnits(grossRevenue, "Revenue");
        RequireMinorUnits(operatingExpenses, "Expenses");

        GrossRevenue = grossRevenue;
        OperatingExpenses = operatingExpenses;
        Note = note ?? Note;
    }

    /// <summary>
    /// Replaces the whole breakdown with <paramref name="lines"/>. Replaced wholesale rather than
    /// patched row by row: it is edited as one table, and an empty list clears it.
    /// </summary>
    /// <remarks>
    /// The lines are NOT forced to add up to <see cref="GrossRevenue"/> and
    /// <see cref="OperatingExpenses"/>. They are a breakdown offered for explanation, and a partial
    /// one is more useful than none — a caller that wants to flag a discrepancy compares the sums
    /// itself, exactly as the room breakdown works.
    /// </remarks>
    public void ReplaceLines(IEnumerable<(OperatingLineKind Kind, string Label, decimal Amount)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (Status != OperatingPeriodStatus.Draft)
            throw new DomainException("Only a draft period can be revised.");

        var replacement = lines
            .Select((l, i) => OperatingPeriodLine.Create(Id, l.Kind, l.Label, l.Amount, i))
            .ToList();

        _lines.Clear();
        _lines.AddRange(replacement);
    }

    /// <summary>
    /// Confirms the figures, freezing them and allowing a distribution to be declared from the period.
    /// </summary>
    /// <remarks>
    /// The confirmer may not be the person who reported the figures. This is the separation the
    /// management company asked for in so many words, and enforcing it in the aggregate is what keeps
    /// it from depending on whichever endpoint happens to be calling.
    /// </remarks>
    public void Confirm(Guid confirmedByUserId, DateTime nowUtc)
    {
        if (confirmedByUserId == Guid.Empty)
            throw new DomainException("ConfirmedByUserId is required.");
        if (Status != OperatingPeriodStatus.Draft)
            throw new DomainException("Only a draft period can be confirmed.");
        if (confirmedByUserId == ReportedByUserId)
            throw new DomainException(
                "The figures must be confirmed by someone other than the person who reported them.");

        Status = OperatingPeriodStatus.Confirmed;
        ConfirmedByUserId = confirmedByUserId;
        ConfirmedAtUtc = nowUtc;
    }

    /// <summary>
    /// Records that <paramref name="payoutRunId"/> was declared from this period, and how much of it
    /// was distributed.
    /// </summary>
    /// <remarks>
    /// A period pays out once. Declaring twice from the same figures is the distribution equivalent of
    /// dividing one register twice — the money would go out again against income that only existed
    /// once.
    /// </remarks>
    public void MarkDistributed(Guid payoutRunId, decimal declaredAmount)
    {
        if (payoutRunId == Guid.Empty)
            throw new DomainException("PayoutRunId is required.");
        if (Status == OperatingPeriodStatus.Draft)
            throw new DomainException("A distribution cannot be declared from an unconfirmed period.");
        if (Status == OperatingPeriodStatus.Distributed)
            throw new DomainException("A distribution has already been declared from this period.");
        if (declaredAmount <= 0)
            throw new DomainException("The declared amount must be positive.");
        if (declaredAmount > NetIncome)
            throw new DomainException(
                $"The declared amount exceeds the period's net income ({NetIncome:0.##} {Currency}).");

        Status = OperatingPeriodStatus.Distributed;
        PayoutRunId = payoutRunId;
    }

    private static void RequireMinorUnits(decimal value, string what)
    {
        if (decimal.Round(value, MinorUnitScale) != value)
            throw new DomainException($"{what} must be given to {MinorUnitScale} decimal places.");
    }
}
