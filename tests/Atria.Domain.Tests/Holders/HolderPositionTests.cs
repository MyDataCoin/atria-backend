using Atria.Domain.Common;
using Atria.Domain.Holders;
using FluentAssertions;

namespace Atria.Domain.Tests.Holders;

/// <summary>
/// Covers the current-state holder projection: it is keyed by wallet, sync is idempotent, and a
/// position can outlive its allowlist membership (a blocked investor keeps shares we can't reclaim).
/// </summary>
public sealed class HolderPositionTests
{
    private static readonly DateTime T0 = new(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc);
    private const string Wallet = "0x1111111111111111111111111111111111111111";

    private static HolderPosition New(long tokens = 10)
        => HolderPosition.Create(Guid.NewGuid(), Wallet, tokens, Guid.NewGuid(), true, HolderSource.OurRecords, T0);

    [Fact]
    public void Create_sets_all_fields()
    {
        var propertyId = Guid.NewGuid();
        var investorId = Guid.NewGuid();

        var pos = HolderPosition.Create(propertyId, Wallet, 42, investorId, true, HolderSource.OurRecords, T0);

        pos.PropertyId.Should().Be(propertyId);
        pos.WalletAddress.Should().Be(Wallet);
        pos.TokenCount.Should().Be(42);
        pos.InvestorId.Should().Be(investorId);
        pos.IsAllowlisted.Should().BeTrue();
        pos.Source.Should().Be(HolderSource.OurRecords);
        pos.LastSyncedAtUtc.Should().Be(T0);
    }

    [Fact]
    public void Create_rejects_negative_token_count()
    {
        var act = () => HolderPosition.Create(Guid.NewGuid(), Wallet, -1, null, true, HolderSource.OurRecords, T0);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_rejects_blank_wallet()
    {
        var act = () => HolderPosition.Create(Guid.NewGuid(), "  ", 1, null, true, HolderSource.OurRecords, T0);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Sync_is_idempotent()
    {
        var pos = New(10);
        var t1 = T0.AddHours(1);

        pos.Sync(25, HolderSource.Chain, t1);
        pos.Sync(25, HolderSource.Chain, t1);

        pos.TokenCount.Should().Be(25);
        pos.Source.Should().Be(HolderSource.Chain);
        pos.LastSyncedAtUtc.Should().Be(t1);
    }

    [Fact]
    public void Position_can_keep_shares_after_falling_off_the_allowlist()
    {
        var pos = New(10);

        pos.SetAllowlisted(false);

        pos.IsAllowlisted.Should().BeFalse();
        pos.TokenCount.Should().Be(10); // shares are not clawed back — we hold no keys
    }

    [Fact]
    public void Increase_accumulates_shares_and_marks_our_records()
    {
        var pos = HolderPosition.Create(Guid.NewGuid(), Wallet, 10, Guid.NewGuid(), true, HolderSource.Chain, T0);
        var t1 = T0.AddHours(2);

        pos.Increase(15, t1);

        pos.TokenCount.Should().Be(25);
        pos.Source.Should().Be(HolderSource.OurRecords);
        pos.LastSyncedAtUtc.Should().Be(t1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Increase_rejects_non_positive(long count)
    {
        var act = () => New().Increase(count, T0);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void LinkInvestor_can_clear_an_unresolved_address()
    {
        var pos = New();

        pos.LinkInvestor(null);

        pos.InvestorId.Should().BeNull();
    }
}
