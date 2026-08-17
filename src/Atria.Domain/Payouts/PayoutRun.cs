using Atria.Domain.Common;
using Atria.Domain.Holders;
using Atria.Domain.Investments;

namespace Atria.Domain.Payouts;

/// <summary>
/// One distribution to the holders of an issue: what was declared, who it is owed to, and whether it
/// reached them.
/// </summary>
/// <remarks>
/// <para>
/// Computed against a frozen <see cref="HolderSnapshot"/>, never against the live register. Holdings
/// move; a distribution has to be the same number today and at an audit two years from now, and the
/// only way to have that is to name the cut it was computed from.
/// </para>
/// <para>
/// The platform does not move money — that is settled outside, by bank transfer or on chain. What
/// lives here is the arithmetic, the second signature that authorised it, and the reference that came
/// back from each payment. Recording is deliberately separate from paying, exactly as it is for
/// refunds: an obligation nobody has written down is one nobody settles.
/// </para>
/// </remarks>
public sealed class PayoutRun : AggregateRoot
{
    /// <summary>
    /// Fractional digits money is divided to. Every currency the platform deals in is a
    /// two-decimal currency; the whole distribution is computed in these units so that division
    /// never leaves a fraction of a unit unaccounted for.
    /// </summary>
    public const int MinorUnitScale = 2;

    public Guid PropertyId { get; private set; }

    /// <summary>The frozen register this distribution was computed against.</summary>
    public Guid SnapshotId { get; private set; }

    /// <summary>The cut the snapshot was taken at, copied here so a run explains itself.</summary>
    public DateTime SnapshotAtUtc { get; private set; }

    public PayoutKind Kind { get; private set; }
    public PayoutMethod Method { get; private set; }

    /// <summary>The total declared for distribution. The lines always add up to exactly this.</summary>
    public decimal DeclaredAmount { get; private set; }

    public string Currency { get; private set; } = null!;

    public PayoutRunStatus Status { get; private set; }

    /// <summary>Shares the distribution was divided across — the snapshot's total.</summary>
    public decimal TotalTokens { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>What this distribution is, in the operator's words. Carried into the audit trail.</summary>
    public string? Note { get; private set; }

    /// <summary>Why the run was called off, when it was.</summary>
    public string? CancellationReason { get; private set; }

    private readonly List<PayoutItem> _items = new();
    public IReadOnlyCollection<PayoutItem> Items => _items.AsReadOnly();

    private PayoutRun() { }

    /// <summary>
    /// Divides a declared amount across the holders of a frozen register.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every line must have a known investor. An address the register cannot tie to a person is a
    /// signal for investigation, and a distribution is the worst moment to discover one: the money
    /// would either go to someone we cannot name or quietly not go out at all. The run refuses to
    /// exist until the register is fixed.
    /// </para>
    /// <para>
    /// The division is done in minor units by the largest-remainder method, with the leftover units
    /// handed to the largest holdings and ties broken by address. Simple rounding per line would
    /// leave the sum short or over by a few units — money that either never reaches a holder or is
    /// paid twice — and any tie-break that depends on iteration order would make the same declared
    /// amount split differently on a rerun.
    /// </para>
    /// </remarks>
    public static PayoutRun Create(
        HolderSnapshot snapshot, PayoutKind kind, PayoutMethod method, decimal declaredAmount,
        string currency, Guid createdByUserId, string? note)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Purpose != SnapshotPurpose.Payout)
            throw new DomainException(
                "A distribution must be computed against a snapshot taken for that purpose.");
        if (declaredAmount <= 0)
            throw new DomainException("The declared amount must be positive.");

        // Refused rather than rounded. The lines are divided in minor units, so a third decimal in
        // the declared total would leave the run permanently short of itself: the lines would add up
        // to one number and DeclaredAmount would report another, and OutstandingAmount would never
        // reach zero however many holders were paid. Quietly rounding someone's declared
        // distribution is the other way to get there, and it is worse — nobody would be told.
        if (decimal.Round(declaredAmount, MinorUnitScale) != declaredAmount)
            throw new DomainException(
                $"The declared amount must be given to {MinorUnitScale} decimal places.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");
        if (createdByUserId == Guid.Empty)
            throw new DomainException("CreatedByUserId is required.");
        if (snapshot.TotalTokens <= 0 || snapshot.Rows.Count == 0)
            throw new DomainException("The register holds no shares to distribute across.");
        if (snapshot.Rows.Any(r => r.InvestorId is null))
            throw new DomainException(
                "The register has holding addresses with no investor behind them; "
                + "resolve them before distributing.");

        var run = new PayoutRun
        {
            Id = Guid.NewGuid(),
            PropertyId = snapshot.PropertyId,
            SnapshotId = snapshot.Id,
            SnapshotAtUtc = snapshot.SnapshotAtUtc,
            Kind = kind,
            Method = method,
            DeclaredAmount = declaredAmount,
            Currency = currency,
            Status = PayoutRunStatus.Draft,
            TotalTokens = snapshot.TotalTokens,
            CreatedByUserId = createdByUserId,
            Note = note
        };

        foreach (var (row, amount) in Allocate(snapshot, declaredAmount))
            run._items.Add(PayoutItem.Create(
                run.Id, row.InvestorId!.Value, row.WalletAddress, row.TokenCount, amount));

        return run;
    }

    /// <summary>
    /// Splits <paramref name="declaredAmount"/> across the register in exact proportion to holdings,
    /// with the remainder distributed by largest fractional part.
    /// </summary>
    private static IEnumerable<(HolderSnapshotRow Row, decimal Amount)> Allocate(
        HolderSnapshot snapshot, decimal declaredAmount)
    {
        var factor = Pow10(MinorUnitScale);
        var totalMinor = decimal.ToInt64(decimal.Round(declaredAmount * factor, 0, MidpointRounding.ToEven));

        // Holdings are fractional, so both sides of the ratio go to their integer minor units first
        // (TokenAmount.Scale decimals). That keeps the whole split in exact integer arithmetic —
        // decimal division here would round every line and the parts would stop summing to the whole.
        var totalTokens = TokenAmount.ToMinor(snapshot.TotalTokens);

        // The floor each line certainly gets, and what it lost to the floor. Integer arithmetic
        // throughout: the exact numerator is tokens * totalMinor, which stays whole.
        var lines = snapshot.Rows
            .Select(row =>
            {
                var numerator = TokenAmount.ToMinor(row.TokenCount) * totalMinor;
                var floor = (long)(numerator / totalTokens);
                var remainder = (long)(numerator % totalTokens);
                return (Row: row, Floor: floor, Remainder: remainder);
            })
            .ToList();

        var leftover = totalMinor - lines.Sum(l => l.Floor);

        // The leftover units go to the lines that lost the most to the floor. Ties go to the larger
        // holding, then to the lower address — a total order, so the same input always splits the
        // same way no matter what order the rows arrived in.
        // Each line loses less than a whole unit to the floor, so the leftover is always smaller than
        // the number of lines: one extra unit each, to the first `leftover` of them.
        var extra = lines
            .OrderByDescending(l => l.Remainder)
            .ThenByDescending(l => l.Row.TokenCount)
            .ThenBy(l => l.Row.WalletAddress, StringComparer.Ordinal)
            .Take((int)leftover)
            .Select(l => l.Row.Id)
            .ToHashSet();

        foreach (var line in lines)
        {
            var minor = line.Floor + (extra.Contains(line.Row.Id) ? 1 : 0);
            yield return (line.Row, minor / factor);
        }
    }

    private static decimal Pow10(int exponent)
    {
        var value = 1m;
        for (var i = 0; i < exponent; i++) value *= 10m;
        return value;
    }

    /// <summary>
    /// Opens the run for settlement. Called only by the dual-approval executor: a distribution is the
    /// clearest case of one account moving money, so it never happens on one signature.
    /// </summary>
    public void Approve(DateTime nowUtc)
    {
        if (Status != PayoutRunStatus.Draft)
            throw new DomainException("Only a draft distribution can be approved.");

        Status = PayoutRunStatus.Approved;
        ApprovedAtUtc = nowUtc;
    }

    /// <summary>Calls the run off. Only before any money has moved.</summary>
    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A cancellation reason is required.");
        if (Status is PayoutRunStatus.Completed or PayoutRunStatus.Cancelled)
            throw new DomainException("This distribution is already closed.");
        if (_items.Any(i => i.Status == PayoutItemStatus.Paid))
            throw new DomainException(
                "Part of this distribution has already been paid; it cannot be cancelled.");

        Status = PayoutRunStatus.Cancelled;
        CancellationReason = reason;
    }

    /// <summary>Records that one holder was paid.</summary>
    public void SettleItem(Guid itemId, string settlementReference, DateTime nowUtc)
    {
        RequireApproved();
        ItemOrThrow(itemId).Settle(settlementReference, nowUtc);
    }

    /// <summary>Records that one holder's payment could not be delivered.</summary>
    public void FailItem(Guid itemId, string reason)
    {
        RequireApproved();
        ItemOrThrow(itemId).Fail(reason);
    }

    /// <summary>
    /// Closes the run once every line has been dealt with.
    /// </summary>
    /// <remarks>
    /// A line left pending blocks closure. Failed lines do not: a payment that bounced is a known,
    /// recorded outcome someone has to chase, whereas a pending line means nobody has looked at it —
    /// and closing over it would leave a holder silently unpaid inside a distribution marked done.
    /// </remarks>
    public void Complete(DateTime nowUtc)
    {
        RequireApproved();

        if (_items.Any(i => i.Status == PayoutItemStatus.Pending))
            throw new DomainException(
                "Some holders have neither been paid nor recorded as failed.");

        Status = PayoutRunStatus.Completed;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>What has actually been paid out so far.</summary>
    public decimal PaidAmount => _items.Where(i => i.Status == PayoutItemStatus.Paid).Sum(i => i.Amount);

    /// <summary>What is still owed — pending and failed lines alike.</summary>
    public decimal OutstandingAmount => DeclaredAmount - PaidAmount;

    private void RequireApproved()
    {
        if (Status != PayoutRunStatus.Approved)
            throw new DomainException(
                "This distribution is not open for settlement; it must be approved first.");
    }

    private PayoutItem ItemOrThrow(Guid itemId)
        => _items.FirstOrDefault(i => i.Id == itemId)
           ?? throw new DomainException("This holder is not part of the distribution.");
}
