using Atria.Domain.Common;
using Atria.Domain.Holders;
using FluentAssertions;

namespace Atria.Domain.Tests.Holders;

/// <summary>
/// Covers the immutable snapshot: header totals are derived from the rows, shares sum to the whole,
/// zero-balance entries are dropped, and the same holdings on the same cut reproduce the same snapshot.
/// </summary>
public sealed class HolderSnapshotTests
{
    private static readonly DateTime Cut = new(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);

    private static readonly HolderSnapshotEntry[] Holdings =
    {
        new("0xaaaa000000000000000000000000000000000000", 30, Guid.NewGuid()),
        new("0xbbbb000000000000000000000000000000000000", 70, Guid.NewGuid())
    };

    private static HolderSnapshot Build(IEnumerable<HolderSnapshotEntry> entries, long? block = 123)
        => HolderSnapshot.Create(Guid.NewGuid(), Cut, SnapshotPurpose.Payout, block, Guid.NewGuid(), entries);

    [Fact]
    public void Create_derives_header_totals_from_rows()
    {
        var snap = Build(Holdings);

        snap.TotalTokens.Should().Be(100);
        snap.AddressCount.Should().Be(2);
        snap.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void Shares_are_token_count_over_total()
    {
        var snap = Build(Holdings);

        snap.Rows.Single(r => r.TokenCount == 30).Share.Should().Be(0.3m);
        snap.Rows.Single(r => r.TokenCount == 70).Share.Should().Be(0.7m);
        snap.Rows.Sum(r => r.Share).Should().Be(1.0m);
    }

    [Fact]
    public void Zero_balance_entries_are_dropped()
    {
        var entries = new[]
        {
            new HolderSnapshotEntry("0xaaaa000000000000000000000000000000000000", 50, null),
            new HolderSnapshotEntry("0xcccc000000000000000000000000000000000000", 0, null)
        };

        var snap = Build(entries);

        snap.AddressCount.Should().Be(1);
        snap.Rows.Should().OnlyContain(r => r.TokenCount > 0);
    }

    [Fact]
    public void Same_holdings_on_same_cut_reproduce_the_same_rows()
    {
        var propertyId = Guid.NewGuid();
        var creator = Guid.NewGuid();

        var a = HolderSnapshot.Create(propertyId, Cut, SnapshotPurpose.Payout, 123, creator, Holdings);
        var b = HolderSnapshot.Create(propertyId, Cut, SnapshotPurpose.Payout, 123, creator, Holdings.Reverse());

        a.TotalTokens.Should().Be(b.TotalTokens);
        a.AddressCount.Should().Be(b.AddressCount);
        // Rows are ordered by address, so the layout is identical regardless of input order.
        a.Rows.Select(r => (r.WalletAddress, r.TokenCount, r.Share))
            .Should().Equal(b.Rows.Select(r => (r.WalletAddress, r.TokenCount, r.Share)));
    }

    [Fact]
    public void BlockNumber_is_null_until_chain_reading_exists()
    {
        var snap = Build(Holdings, block: null);
        snap.BlockNumber.Should().BeNull();
    }

    [Fact]
    public void Create_rejects_negative_block_number()
    {
        var act = () => Build(Holdings, block: -1);
        act.Should().Throw<DomainException>();
    }
}
