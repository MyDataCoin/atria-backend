using Atria.Domain.Common;
using Atria.Domain.Holders;
using Atria.Domain.Payouts;
using FluentAssertions;

namespace Atria.Domain.Tests.Payouts;

/// <summary>
/// A distribution to holders. The arithmetic is the whole point: the lines must add up to exactly
/// what was declared, and the same declared amount over the same register must split the same way
/// every time it is computed.
/// </summary>
public sealed class PayoutRunTests
{
    private static readonly DateTime Cut = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Operator = Guid.NewGuid();

    private static HolderSnapshot Register(params long[] holdings)
    {
        var entries = holdings.Select((tokens, i) => new HolderSnapshotEntry(
            $"0x{i:D40}", tokens, Guid.NewGuid()));

        return HolderSnapshot.Create(
            Guid.NewGuid(), Cut, SnapshotPurpose.Payout, 1_000_000, Operator, entries);
    }

    private static PayoutRun Run(HolderSnapshot snapshot, decimal declared = 100_000m)
        => PayoutRun.Create(
            snapshot, PayoutKind.Dividend, PayoutMethod.BankTransfer, declared, "KGS", Operator,
            "Дивиденды за II квартал 2026");

    [Fact]
    public void Each_holder_gets_their_proportion()
    {
        var run = Run(Register(500, 300, 200), declared: 10_000m);

        run.Items.Select(i => i.Amount).Should().BeEquivalentTo(new[] { 5_000m, 3_000m, 2_000m });
        run.TotalTokens.Should().Be(1_000);
    }

    /// <summary>
    /// The case that matters: three holders and a sum that does not divide. Rounding each line on its
    /// own would leave the distribution a kopeck short of what was declared.
    /// </summary>
    [Fact]
    public void The_lines_add_up_to_exactly_what_was_declared()
    {
        var run = Run(Register(1, 1, 1), declared: 100m);

        run.Items.Sum(i => i.Amount).Should().Be(100m);
        run.Items.Select(i => i.Amount).Should().BeEquivalentTo(new[] { 33.34m, 33.33m, 33.33m });
    }

    [Theory]
    [InlineData(new long[] { 3, 7, 11 })]
    [InlineData(new long[] { 1, 1, 1, 1, 1, 1, 1 })]
    [InlineData(new long[] { 999_999, 1 })]
    [InlineData(new long[] { 17, 17, 17, 17, 17, 17 })]
    public void No_money_is_created_or_lost_however_the_register_is_shaped(long[] holdings)
    {
        var run = Run(Register(holdings), declared: 1_234.57m);

        run.Items.Sum(i => i.Amount).Should().Be(1_234.57m);
        run.Items.Should().OnlyContain(i => i.Amount >= 0);
    }

    /// <summary>
    /// A distribution recomputed must be the same distribution. Anything that depended on iteration
    /// order would pay a different holder the spare kopeck on a rerun.
    /// </summary>
    [Fact]
    public void The_same_register_and_amount_always_split_the_same_way()
    {
        var snapshot = Register(5, 3, 3, 1);

        var first = Run(snapshot, declared: 1_000m);
        var second = Run(snapshot, declared: 1_000m);

        first.Items.OrderBy(i => i.WalletAddress, StringComparer.Ordinal).Select(i => i.Amount)
            .Should().Equal(
                second.Items.OrderBy(i => i.WalletAddress, StringComparer.Ordinal).Select(i => i.Amount));
    }

    /// <summary>The spare units go to the holders who lost the most to the floor, not to the first row.</summary>
    [Fact]
    public void The_remainder_goes_to_the_lines_that_lost_the_most_to_rounding()
    {
        // 100.00 over 6 shares split 3/2/1 → 50.00 / 33.333… / 16.666…; one kopeck is left over and
        // belongs to the line whose fraction was largest.
        var run = Run(Register(3, 2, 1), declared: 100m);

        var byTokens = run.Items.ToDictionary(i => i.TokenCount, i => i.Amount);
        byTokens[3].Should().Be(50m);
        byTokens[2].Should().Be(33.33m);
        byTokens[1].Should().Be(16.67m);
        run.Items.Sum(i => i.Amount).Should().Be(100m);
    }

    /// <summary>
    /// Paying an address the register cannot tie to a person means either paying someone we cannot
    /// name or silently not paying them. Neither is acceptable inside a distribution.
    /// </summary>
    [Fact]
    public void An_unidentified_holder_stops_the_distribution_being_created()
    {
        var snapshot = HolderSnapshot.Create(
            Guid.NewGuid(), Cut, SnapshotPurpose.Payout, null, Operator,
            [
                new HolderSnapshotEntry("0xaaa", 10, Guid.NewGuid()),
                new HolderSnapshotEntry("0xbbb", 5, null)
            ]);

        var act = () => Run(snapshot);

        act.Should().Throw<DomainException>().WithMessage("*investor*");
    }

    /// <summary>
    /// A reporting snapshot is a statement of the register, not a payment basis. Computing money
    /// against it would tie the distribution to a cut taken for another reason entirely.
    /// </summary>
    [Fact]
    public void A_reporting_snapshot_is_not_a_basis_for_paying_money()
    {
        var snapshot = HolderSnapshot.Create(
            Guid.NewGuid(), Cut, SnapshotPurpose.Reporting, null, Operator,
            [new HolderSnapshotEntry("0xaaa", 10, Guid.NewGuid())]);

        var act = () => PayoutRun.Create(
            snapshot, PayoutKind.Dividend, PayoutMethod.Wallet, 100m, "KGS", Operator, null);

        act.Should().Throw<DomainException>().WithMessage("*purpose*");
    }

    /// <summary>
    /// A third decimal cannot be divided into minor units, so the lines would add up to one number
    /// while the run reported another and the outstanding balance never reached zero.
    /// </summary>
    [Fact]
    public void An_amount_finer_than_a_minor_unit_is_refused_rather_than_rounded()
    {
        var act = () => Run(Register(1, 1), declared: 100.005m);

        act.Should().Throw<DomainException>().WithMessage("*decimal places*");
    }

    [Fact]
    public void A_new_distribution_is_a_draft_and_pays_nobody_yet()
    {
        var run = Run(Register(1, 1));

        run.Status.Should().Be(PayoutRunStatus.Draft);
        run.PaidAmount.Should().Be(0);
        run.OutstandingAmount.Should().Be(100_000m);

        var act = () => run.SettleItem(run.Items.First().Id, "PO-1", Now);
        act.Should().Throw<DomainException>().WithMessage("*approved*");
    }

    [Fact]
    public void Settling_a_line_records_the_reference_that_proves_it()
    {
        var run = Run(Register(1, 1));
        run.Approve(Now);
        var item = run.Items.First();

        run.SettleItem(item.Id, "PO-2026-0042", Now);

        item.Status.Should().Be(PayoutItemStatus.Paid);
        item.SettlementReference.Should().Be("PO-2026-0042");
        item.PaidAtUtc.Should().Be(Now);
        run.PaidAmount.Should().Be(50_000m);
        run.OutstandingAmount.Should().Be(50_000m);
    }

    [Fact]
    public void A_holder_cannot_be_paid_twice()
    {
        var run = Run(Register(1, 1));
        run.Approve(Now);
        var item = run.Items.First();
        run.SettleItem(item.Id, "PO-1", Now);

        var act = () => run.SettleItem(item.Id, "PO-2", Now);

        act.Should().Throw<DomainException>();
    }

    /// <summary>A bounced payment is still owed; it must not quietly shrink the distribution.</summary>
    [Fact]
    public void A_failed_line_stays_owed()
    {
        var run = Run(Register(1, 1));
        run.Approve(Now);
        var item = run.Items.First();

        run.FailItem(item.Id, "Счёт закрыт");

        item.Status.Should().Be(PayoutItemStatus.Failed);
        item.Amount.Should().Be(50_000m);
        run.OutstandingAmount.Should().Be(100_000m);
    }

    [Fact]
    public void A_line_nobody_has_looked_at_blocks_closing_the_distribution()
    {
        var run = Run(Register(1, 1));
        run.Approve(Now);
        run.SettleItem(run.Items.First().Id, "PO-1", Now);

        var act = () => run.Complete(Now);

        act.Should().Throw<DomainException>().WithMessage("*neither*");
    }

    [Fact]
    public void A_distribution_closes_once_every_line_has_an_outcome()
    {
        var run = Run(Register(1, 1));
        run.Approve(Now);
        run.SettleItem(run.Items.First().Id, "PO-1", Now);
        run.FailItem(run.Items.Last().Id, "Счёт закрыт");

        run.Complete(Now);

        run.Status.Should().Be(PayoutRunStatus.Completed);
        run.CompletedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void A_distribution_that_has_paid_someone_cannot_be_called_off()
    {
        var run = Run(Register(1, 1));
        run.Approve(Now);
        run.SettleItem(run.Items.First().Id, "PO-1", Now);

        var act = () => run.Cancel("Передумали");

        act.Should().Throw<DomainException>().WithMessage("*already been paid*");
    }

    [Fact]
    public void An_untouched_draft_can_be_called_off()
    {
        var run = Run(Register(1, 1));

        run.Cancel("Ошибка в объявленной сумме");

        run.Status.Should().Be(PayoutRunStatus.Cancelled);
        run.CancellationReason.Should().Be("Ошибка в объявленной сумме");
    }

    [Fact]
    public void A_distribution_cannot_be_approved_twice()
    {
        var run = Run(Register(1, 1));
        run.Approve(Now);

        var act = () => run.Approve(Now);

        act.Should().Throw<DomainException>();
    }
}
