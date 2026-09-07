using Atria.Domain.Common;
using Atria.Domain.Trading;
using Atria.Domain.Trading.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Trading;

/// <summary>
/// A trade card is the half of a secondary trade the chain cannot tell us: price, trade number,
/// counterparty. Because it arrives from outside, the rules here are about what may not be believed
/// — a trade with no price, a trade with itself on both sides, an off-chain settlement that never
/// says where the shares went, or a "correction" that quietly rewrites what was already reported.
/// </summary>
public sealed class ExchangeTradeTests
{
    private const string Seller = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Buyer = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Contract = "0xcccccccccccccccccccccccccccccccccccccccc";
    private const string TxHash = "0xdeadbeef";

    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static ExchangeTrade NewTrade(
        string eventId = "EV-1",
        long sequenceNo = 1,
        string? txHash = TxHash,
        long tokenCount = 10,
        decimal price = 120m,
        string? custodyAddress = null,
        string? seller = Seller,
        string? buyer = Buyer)
        => ExchangeTrade.Settle(
            eventId, sequenceNo, "TRD-77", Guid.NewGuid(), "bsc", Contract, txHash, 100,
            seller!, buyer!, tokenCount, price, "KGS", Now, Now, counterpartyVasp: null,
            beneficiaryName: null, custodyAddress, Now);

    [Fact]
    public void Settle_RecordsTheTradingContextAndWaitsForTheChain()
    {
        var trade = NewTrade();

        trade.EventType.Should().Be(ExchangeTradeType.Settled);
        trade.TokenCount.Should().Be(10);
        trade.PricePerToken.Should().Be(120m);

        // The value the register could not compute on its own: 10 shares at the executed price,
        // not at the issue's nominal one.
        trade.TotalValue.Should().Be(1_200m);

        // Reported with a transaction hash, so it owes a match and does not have one yet.
        trade.MatchState.Should().Be(TradeMatchState.AwaitingChain);
    }

    [Fact]
    public void Settle_RaisesTheEventCarryingTheExecutionPrice()
    {
        var trade = NewTrade();

        var settled = trade.DomainEvents.OfType<ExchangeTradeSettledEvent>().Single();
        settled.PricePerToken.Should().Be(120m);
        settled.ExchangeEventId.Should().Be("EV-1");
    }

    [Fact]
    public void Settle_RefusesATradeWithoutAnExecutionPrice()
    {
        // The entire point of the integration. A card with no price leaves every downstream
        // valuation on the nominal figure, which is what we are trying to stop doing.
        var act = () => NewTrade(price: 0m);

        act.Should().Throw<DomainException>().WithMessage("*execution price*");
    }

    [Fact]
    public void Settle_RefusesATradeWithTheSameAddressOnBothSides()
    {
        var act = () => NewTrade(seller: Seller, buyer: Seller.ToUpperInvariant());

        act.Should().Throw<DomainException>().WithMessage("*same address*");
    }

    [Fact]
    public void Settle_RefusesAFractionalOrEmptyShareCount()
    {
        // decimals() is zero, so a trade of no shares is not a small trade — it is a broken record.
        var act = () => NewTrade(tokenCount: 0);

        act.Should().Throw<DomainException>().WithMessage("*positive whole number*");
    }

    [Fact]
    public void Settle_OffChainMustSayWhereTheSharesAreHeld()
    {
        // Otherwise the shares are unaccounted for: no transfer to read, and no address to attribute
        // them to in the register.
        var act = () => NewTrade(txHash: null, custodyAddress: null);

        act.Should().Throw<DomainException>().WithMessage("*address the shares are held on*");
    }

    [Fact]
    public void Settle_OffChainWithACustodyAddressNeverAwaitsAMatch()
    {
        var trade = NewTrade(txHash: null, custodyAddress: "0xomnibus");

        // Nothing will ever appear on chain for this trade, so leaving it "awaiting" would
        // manufacture a permanent discrepancy in every reconciliation.
        trade.MatchState.Should().Be(TradeMatchState.OffChain);
        trade.CustodyAddress.Should().Be("0xomnibus");
    }

    [Fact]
    public void MarkMatched_ClearsAnEarlierDisagreement()
    {
        var trade = NewTrade();
        trade.MarkMismatched("не сходится", Now);

        trade.MarkMatched(Now);

        trade.MatchState.Should().Be(TradeMatchState.Matched);
        trade.MatchDetail.Should().BeNull();
    }

    [Fact]
    public void MarkMismatched_KeepsTheCardAsReported()
    {
        var trade = NewTrade();

        trade.MarkMismatched("Биржа сообщила 10 долей, в цепочке 4.", Now);

        // The card is evidence of what they told us. Correcting it here would destroy the very
        // discrepancy the incident is about.
        trade.MatchState.Should().Be(TradeMatchState.Mismatched);
        trade.TokenCount.Should().Be(10);
        trade.MatchDetail.Should().Contain("в цепочке 4");
    }

    [Fact]
    public void Reverse_CopiesTheOriginalAndPointsBackAtIt()
    {
        var original = NewTrade();

        var reversal = ExchangeTrade.Reverse("EV-2", 2, original.EventId, original, Now);

        reversal.EventType.Should().Be(ExchangeTradeType.Cancelled);
        reversal.ReversesEventId.Should().Be("EV-1");
        reversal.ExchangeTradeId.Should().Be(original.ExchangeTradeId);
        reversal.MatchState.Should().Be(TradeMatchState.Reversed);
    }

    [Fact]
    public void MarkReversedBy_FlagsTheOriginalWithoutErasingIt()
    {
        var original = NewTrade();

        original.MarkReversedBy("EV-2");

        original.IsReversed.Should().BeTrue();
        original.MatchState.Should().Be(TradeMatchState.Reversed);
        original.MatchDetail.Should().Contain("EV-2");

        // Still holds its own numbers: reporting that already went out on this trade has to stay
        // answerable, including the fact that it was later taken back.
        original.PricePerToken.Should().Be(120m);
        original.DomainEvents.OfType<ExchangeTradeReversedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void AReversalCannotItselfBeReversed()
    {
        var original = NewTrade();
        var reversal = ExchangeTrade.Reverse("EV-2", 2, original.EventId, original, Now);

        var act = () => reversal.MarkReversedBy("EV-3");

        act.Should().Throw<DomainException>().WithMessage("*settled trade*");
    }

    [Fact]
    public void AReversedTradeIgnoresLaterMatching()
    {
        var original = NewTrade();
        original.MarkReversedBy("EV-2");

        original.MarkMatched(Now);

        // A reversed trade owes no match; letting the sweep flip it back to Matched would hide the
        // reversal from every report that reads MatchState.
        original.MatchState.Should().Be(TradeMatchState.Reversed);
    }
}
