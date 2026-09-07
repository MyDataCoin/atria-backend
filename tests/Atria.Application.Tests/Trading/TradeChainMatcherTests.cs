using Atria.Application.Abstractions;
using Atria.Application.Trading;
using Atria.Domain.Investments;
using Atria.Domain.Trading;
using Atria.Domain.TravelRule;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atria.Application.Tests.Trading;

/// <summary>
/// The matcher decides whether a reported trade and an observed transfer are the same event. Getting
/// this wrong in either direction is costly: a false match attaches someone else's price to a
/// transfer, and a false mismatch raises an incident against an exchange that did nothing wrong.
/// </summary>
public sealed class TradeChainMatcherTests
{
    private const string Chain = "bsc-testnet";
    private const string Contract = "0xcccccccccccccccccccccccccccccccccccccccc";
    private const string Seller = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Buyer = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string TxHash = "0xdeadbeef";

    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly IExchangeTradeRepository _trades = Substitute.For<IExchangeTradeRepository>();
    private readonly ITokenReader _tokens = Substitute.For<ITokenReader>();
    private readonly ITravelRuleRepository _travelRule = Substitute.For<ITravelRuleRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    private readonly Property _property;

    public TradeChainMatcherTests()
    {
        _clock.UtcNow.Returns(Now);

        _property = Property.Create("Borsan Residence", null, null, 7_260_000m, 100m, 72_600, "KGS");
        _property.Publish();
        _property.SetTokenContract(Contract, Chain, "0xissuer", 100);

        _properties.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_property);
    }

    private TradeChainMatcher NewMatcher()
        => new(_trades, _tokens, _travelRule, _properties, _clock,
            NullLogger<TradeChainMatcher>.Instance);

    private ExchangeTrade NewTrade(
        long tokenCount = 10, string? txHash = TxHash, string? beneficiaryName = null,
        string? custodyAddress = null)
        => ExchangeTrade.Settle(
            "EV-1", 1, "TRD-77", _property.Id, Chain, Contract, txHash, 100, Seller, Buyer,
            tokenCount, 120m, "KGS", Now, Now, counterpartyVasp: "Exchange Ltd",
            beneficiaryName: beneficiaryName, custodyAddress, Now);

    private void GivenChainTransfers(params TokenTransfer[] transfers)
        => _tokens.GetTransfersByTransactionAsync(
                Chain, Contract, TxHash, Arg.Any<CancellationToken>())
            .Returns(transfers);

    [Fact]
    public async Task AgreeingOnPartiesAndAmountIsAMatch()
    {
        var trade = NewTrade(tokenCount: 10);
        GivenChainTransfers(new TokenTransfer(Seller, Buyer, 10, 100, TxHash));

        await NewMatcher().MatchAsync(trade, CancellationToken.None);

        trade.MatchState.Should().Be(TradeMatchState.Matched);
        trade.MatchedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task AddressCasingDoesNotDecideTheOutcome()
    {
        // The same address is legitimately written checksummed or lowercase. Comparing case
        // sensitively would invent an incident out of pure formatting.
        var trade = NewTrade();
        GivenChainTransfers(
            new TokenTransfer(Seller.ToUpperInvariant(), Buyer.ToUpperInvariant(), 10, 100, TxHash));

        await NewMatcher().MatchAsync(trade, CancellationToken.None);

        trade.MatchState.Should().Be(TradeMatchState.Matched);
    }

    [Fact]
    public async Task ADifferentAmountOnTheSamePairIsAMismatchThatNamesBothNumbers()
    {
        var trade = NewTrade(tokenCount: 10);
        GivenChainTransfers(new TokenTransfer(Seller, Buyer, 4, 100, TxHash));

        await NewMatcher().MatchAsync(trade, CancellationToken.None);

        trade.MatchState.Should().Be(TradeMatchState.Mismatched);
        trade.MatchDetail.Should().Contain("10").And.Contain("4");
    }

    [Fact]
    public async Task TheRightTransferIsPickedOutOfABatchedTransaction()
    {
        // One settlement transaction can carry several trades. Matching the first log rather than
        // the right one would attach this trade's price to someone else's transfer.
        var trade = NewTrade(tokenCount: 10);
        GivenChainTransfers(
            new TokenTransfer("0xother", "0xelse", 99, 100, TxHash),
            new TokenTransfer(Seller, Buyer, 10, 100, TxHash));

        await NewMatcher().MatchAsync(trade, CancellationToken.None);

        trade.MatchState.Should().Be(TradeMatchState.Matched);
    }

    [Fact]
    public async Task NoTransferFoundYetLeavesTheCardWaiting()
    {
        // The ordinary case: the card arrives before the block is deep enough to be read. Calling
        // that a mismatch would raise an incident on every freshly reported trade.
        var trade = NewTrade();
        GivenChainTransfers();

        await NewMatcher().MatchAsync(trade, CancellationToken.None);

        trade.MatchState.Should().Be(TradeMatchState.AwaitingChain);
    }

    [Fact]
    public async Task AnUnreachableNodeNeverProducesAMismatch()
    {
        var trade = NewTrade();
        _tokens.GetTransfersByTransactionAsync(
                Chain, Contract, TxHash, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<TokenTransfer>>(_ => throw new HttpRequestException("node down"));

        await NewMatcher().MatchAsync(trade, CancellationToken.None);

        // "We could not ask" is not "the answer disagreed".
        trade.MatchState.Should().Be(TradeMatchState.AwaitingChain);
    }

    [Fact]
    public async Task AnOffChainTradeIsNeverCheckedAgainstTheChain()
    {
        var trade = NewTrade(txHash: null, custodyAddress: "0xomnibus");

        await NewMatcher().MatchAsync(trade, CancellationToken.None);

        trade.MatchState.Should().Be(TradeMatchState.OffChain);
        await _tokens.DidNotReceive().GetTransfersByTransactionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMatchCompletesTheTravelRuleDisclosureWithTheCounterparty()
    {
        var trade = NewTrade(beneficiaryName: "Aigul Isaeva");
        GivenChainTransfers(new TokenTransfer(Seller, Buyer, 10, 100, TxHash));

        var message = TravelRuleMessage.Assemble(
            _property.Id, Guid.NewGuid(), TravelRuleDirection.Outgoing, 10, 1_000m, "KGS",
            Seller, Buyer, "Nurlan Osmonov", null, null,
            beneficiaryName: null, counterpartyVasp: null, TxHash);

        _travelRule.FindByTransferAsync(TxHash, Seller, Buyer, Arg.Any<CancellationToken>())
            .Returns(message);

        await NewMatcher().MatchAsync(trade, CancellationToken.None);

        // This is the gap the exchange exists to close: our own KYC can name the sender, never the
        // recipient at another provider.
        message.BeneficiaryName.Should().Be("Aigul Isaeva");
        message.CounterpartyVasp.Should().Be("Exchange Ltd");
    }

    [Fact]
    public async Task ADisclosureAlreadySentIsLeftExactlyAsItWasSent()
    {
        var trade = NewTrade(beneficiaryName: "Aigul Isaeva");
        GivenChainTransfers(new TokenTransfer(Seller, Buyer, 10, 100, TxHash));

        var message = TravelRuleMessage.Assemble(
            _property.Id, Guid.NewGuid(), TravelRuleDirection.Outgoing, 10, 1_000m, "KGS",
            Seller, Buyer, "Nurlan Osmonov", null, null, null, null, TxHash);
        message.MarkSent("{\"ivms\":\"...\"}", "REF-1", Now);

        _travelRule.FindByTransferAsync(TxHash, Seller, Buyer, Arg.Any<CancellationToken>())
            .Returns(message);

        await NewMatcher().MatchAsync(trade, CancellationToken.None);

        // What was disclosed has to stay answerable as disclosed, even when better data arrives.
        message.BeneficiaryName.Should().BeNull();
        trade.MatchState.Should().Be(TradeMatchState.Matched);
    }

    [Fact]
    public async Task MatchOutstandingCountsOnlyTheCardsThatChanged()
    {
        var matched = NewTrade();
        var waiting = ExchangeTrade.Settle(
            "EV-2", 2, "TRD-78", _property.Id, Chain, Contract, "0xnothing", 100, Seller, Buyer,
            5, 120m, "KGS", Now, Now, null, null, null, Now);

        _trades.ListAwaitingChainAsync(_property.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { matched, waiting });

        GivenChainTransfers(new TokenTransfer(Seller, Buyer, 10, 100, TxHash));
        _tokens.GetTransfersByTransactionAsync(
                Chain, Contract, "0xnothing", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TokenTransfer>());

        var changed = await NewMatcher().MatchOutstandingAsync(_property.Id, CancellationToken.None);

        changed.Should().Be(1);
        matched.MatchState.Should().Be(TradeMatchState.Matched);
        waiting.MatchState.Should().Be(TradeMatchState.AwaitingChain);
    }
}
