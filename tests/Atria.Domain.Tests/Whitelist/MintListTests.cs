using Atria.Domain.Common;
using Atria.Domain.Whitelist;
using Atria.Domain.Whitelist.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Whitelist;

public sealed class MintListTests
{
    private const string WalletA = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string WalletB = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Contract = "0xcccccccccccccccccccccccccccccccccccccccc";

    private static WhitelistEntry MintableEntry(Guid propertyId, string wallet, long tokenCount)
    {
        var entry = WhitelistEntry.Queue(
            Guid.NewGuid(), Guid.NewGuid(), propertyId, tokenCount, wallet, DateTime.UtcNow);
        entry.MarkReady(DateTime.UtcNow);
        return entry;
    }

    private static MintList NewList(Guid propertyId, params WhitelistEntry[] entries)
        => MintList.Create(
            "ML-2026-0001", propertyId, Contract, "bsc", Guid.NewGuid(), note: null, entries);

    [Fact]
    public void Create_FreezesOneLinePerRequestAndDerivesTheTotals()
    {
        var propertyId = Guid.NewGuid();
        var first = MintableEntry(propertyId, WalletB, 3);
        var second = MintableEntry(propertyId, WalletA, 15);

        var list = NewList(propertyId, first, second);

        list.Status.Should().Be(MintListStatus.Draft);
        list.ItemCount.Should().Be(2);
        list.TotalTokens.Should().Be(18);
        list.TokenContractAddress.Should().Be(Contract);
        list.TokenChain.Should().Be("bsc");
    }

    [Fact]
    public void Create_OrdersLinesByAddressSoTheSameBatchAlwaysRendersTheSame()
    {
        var propertyId = Guid.NewGuid();
        var b = MintableEntry(propertyId, WalletB, 1);
        var a = MintableEntry(propertyId, WalletA, 1);

        var list = NewList(propertyId, b, a);

        list.Items.Select(i => i.WalletAddress).Should().ContainInOrder(WalletA, WalletB);
    }

    [Fact]
    public void Create_WithNoRequests_Throws()
    {
        var act = () => NewList(Guid.NewGuid());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WhenARequestBelongsToAnotherIssuance_Throws()
    {
        var propertyId = Guid.NewGuid();
        var mine = MintableEntry(propertyId, WalletA, 1);
        var foreign = MintableEntry(Guid.NewGuid(), WalletB, 1);

        var act = () => NewList(propertyId, mine, foreign);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WhenARequestIsNotApproved_Throws()
    {
        var propertyId = Guid.NewGuid();
        var approved = MintableEntry(propertyId, WalletA, 1);
        var pending = WhitelistEntry.Queue(
            Guid.NewGuid(), Guid.NewGuid(), propertyId, 1, WalletB, DateTime.UtcNow);

        var act = () => NewList(propertyId, approved, pending);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkSent_RaisesTheHandoverEvent()
    {
        var propertyId = Guid.NewGuid();
        var list = NewList(propertyId, MintableEntry(propertyId, WalletA, 4));
        var sentAt = DateTime.UtcNow;

        list.MarkSent(sentAt);

        list.Status.Should().Be(MintListStatus.Sent);
        list.SentAtUtc.Should().Be(sentAt);
        var sent = list.DomainEvents.OfType<MintListSentToExchangeEvent>().Single();
        sent.Number.Should().Be("ML-2026-0001");
        sent.PropertyId.Should().Be(propertyId);
        sent.ItemCount.Should().Be(1);
        sent.TotalTokens.Should().Be(4);
    }

    [Fact]
    public void MarkSent_Twice_Throws()
    {
        var propertyId = Guid.NewGuid();
        var list = NewList(propertyId, MintableEntry(propertyId, WalletA, 1));
        list.MarkSent(DateTime.UtcNow);

        var act = () => list.MarkSent(DateTime.UtcNow);

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarkExecuted_RequiresTheBatchToHaveBeenSent()
    {
        var propertyId = Guid.NewGuid();
        var list = NewList(propertyId, MintableEntry(propertyId, WalletA, 1));

        var act = () => list.MarkExecuted(DateTime.UtcNow);

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarkExecuted_ClosesTheBatchAndRaisesItsEvent()
    {
        var propertyId = Guid.NewGuid();
        var list = NewList(propertyId, MintableEntry(propertyId, WalletA, 1));
        list.MarkSent(DateTime.UtcNow);
        var executedAt = DateTime.UtcNow;

        list.MarkExecuted(executedAt);

        list.Status.Should().Be(MintListStatus.Executed);
        list.ExecutedAtUtc.Should().Be(executedAt);
        list.DomainEvents.OfType<MintListExecutedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Cancel_IsAllowedAfterTheBatchWentOut()
    {
        var propertyId = Guid.NewGuid();
        var list = NewList(propertyId, MintableEntry(propertyId, WalletA, 1));
        list.MarkSent(DateTime.UtcNow);

        list.Cancel("Биржа не приняла батч");

        list.Status.Should().Be(MintListStatus.Cancelled);
        list.CancellationReason.Should().Be("Биржа не приняла батч");
        list.DomainEvents.OfType<MintListCancelledEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Cancel_WhenAlreadyExecuted_Throws()
    {
        var propertyId = Guid.NewGuid();
        var list = NewList(propertyId, MintableEntry(propertyId, WalletA, 1));
        list.MarkSent(DateTime.UtcNow);
        list.MarkExecuted(DateTime.UtcNow);

        var act = () => list.Cancel("Передумали");

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Cancel_WithoutReason_Throws()
    {
        var propertyId = Guid.NewGuid();
        var list = NewList(propertyId, MintableEntry(propertyId, WalletA, 1));

        var act = () => list.Cancel("");

        act.Should().Throw<DomainException>();
    }
}
