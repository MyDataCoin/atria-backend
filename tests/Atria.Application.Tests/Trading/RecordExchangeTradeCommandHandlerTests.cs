using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Trading;
using Atria.Application.Trading.Commands;
using Atria.Domain.Investments;
using Atria.Domain.Trading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atria.Application.Tests.Trading;

/// <summary>
/// Ingest is where an outside party's claims meet our own records. The rules that matter are that a
/// redelivery changes nothing, that the issue is resolved from a contract we actually run rather than
/// from anything in the body, and that a malformed event is refused in a way that stops the operator
/// retrying it forever.
/// </summary>
public sealed class RecordExchangeTradeCommandHandlerTests
{
    private const string Chain = "bsc-testnet";
    private const string Contract = "0xcccccccccccccccccccccccccccccccccccccccc";
    private const string Seller = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Buyer = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string TxHash = "0xdeadbeef";

    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly IExchangeTradeRepository _trades = Substitute.For<IExchangeTradeRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly ITradeChainMatcher _matcher = Substitute.For<ITradeChainMatcher>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private readonly Property _property;

    public RecordExchangeTradeCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);

        _property = Property.Create("Borsan Residence", null, null, 7_260_000m, 100m, 72_600, "KGS");
        _property.Publish();
        _property.SetTokenContract(Contract, Chain, "0xissuer", 100);

        _properties.FindByTokenContractAsync(Chain, Contract, Arg.Any<CancellationToken>())
            .Returns(_property);
    }

    private RecordExchangeTradeCommandHandler NewHandler()
        => new(_trades, _properties, _matcher, _clock, _uow,
            NullLogger<RecordExchangeTradeCommandHandler>.Instance);

    private static ExchangeTradeEventDto NewEvent(
        string eventId = "EV-1",
        string type = "trade_settled",
        long sequenceNo = 1,
        decimal price = 120m,
        string? contract = Contract,
        string? reverses = null)
        => new(
            eventId, type, sequenceNo, "TRD-77", Chain, contract, TxHash, 100, Seller, Buyer,
            10, price, "KGS", Now, Now, "Exchange Ltd", "Aigul Isaeva", null, reverses);

    private Task<Result<ExchangeTradeReceipt>> Handle(ExchangeTradeEventDto dto)
        => NewHandler().Handle(new RecordExchangeTradeCommand(dto), CancellationToken.None);

    [Fact]
    public async Task ASettledTradeIsRecordedAndMatchedAgainstTheChain()
    {
        var result = await Handle(NewEvent());

        result.IsSuccess.Should().BeTrue();
        result.Value.Accepted.Should().BeTrue();

        await _trades.Received(1).AddAsync(
            Arg.Is<ExchangeTrade>(t => t.ExchangeTradeId == "TRD-77" && t.PricePerToken == 120m),
            Arg.Any<CancellationToken>());
        await _matcher.Received(1).MatchAsync(Arg.Any<ExchangeTrade>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARedeliveryIsAcknowledgedWithoutRecordingASecondTrade()
    {
        // This is what makes it safe for the operator to retry for 24 hours, which is what we ask of
        // them. Without it, their retry policy silently doubles every trade.
        var already = ExchangeTrade.Settle(
            "EV-1", 1, "TRD-77", _property.Id, Chain, Contract, TxHash, 100, Seller, Buyer,
            10, 120m, "KGS", Now, Now, null, null, null, Now);

        _trades.FindByEventIdAsync("EV-1", Arg.Any<CancellationToken>()).Returns(already);

        var result = await Handle(NewEvent());

        result.IsSuccess.Should().BeTrue();
        result.Value.Accepted.Should().BeFalse();

        await _trades.DidNotReceive().AddAsync(
            Arg.Any<ExchangeTrade>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEventForAContractWeDoNotRunIsRefused()
    {
        _properties.FindByTokenContractAsync(Chain, "0xstranger", Arg.Any<CancellationToken>())
            .Returns((Property?)null);

        var result = await Handle(NewEvent(contract: "0xstranger"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("trade.unknownContract");
    }

    [Fact]
    public async Task AMalformedTradeIsRefusedAsValidationSoTheOperatorStopsRetrying()
    {
        // A permanently broken event must not be answered with a retryable status, or their queue
        // spins on it for a day.
        var result = await Handle(NewEvent(price: 0m));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task AnUnknownEventTypeIsRefused()
    {
        var result = await Handle(NewEvent(type: "trade_partially_maybe"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("trade.unknownEventType");
    }

    [Fact]
    public async Task AnEventWithNoIdIsRefusedBecauseItCannotBeDeduplicated()
    {
        var result = await Handle(NewEvent(eventId: ""));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("trade.eventIdRequired");
    }

    [Fact]
    public async Task AReversalFlagsTheOriginalAndIsStoredAlongsideIt()
    {
        var original = ExchangeTrade.Settle(
            "EV-1", 1, "TRD-77", _property.Id, Chain, Contract, TxHash, 100, Seller, Buyer,
            10, 120m, "KGS", Now, Now, null, null, null, Now);

        _trades.FindSettlementByEventIdAsync("EV-1", Arg.Any<CancellationToken>()).Returns(original);

        var result = await Handle(NewEvent(eventId: "EV-2", type: "trade_cancelled", reverses: "EV-1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchState.Should().Be(TradeMatchState.Reversed);

        original.IsReversed.Should().BeTrue();
        await _trades.Received(1).AddAsync(
            Arg.Is<ExchangeTrade>(t => t.EventType == ExchangeTradeType.Cancelled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReversalNamingNoTradeIsRefused()
    {
        var result = await Handle(
            NewEvent(eventId: "EV-2", type: "trade_cancelled", reverses: null));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("trade.reversalTargetRequired");
    }

    [Fact]
    public async Task AReversalArrivingBeforeItsTradeIsRetryable()
    {
        // Out-of-order delivery is normal on an at-least-once channel. A conflict tells them to
        // redeliver; a 404 would tell them to give up on a trade that is merely still in flight.
        _trades.FindSettlementByEventIdAsync("EV-9", Arg.Any<CancellationToken>())
            .Returns((ExchangeTrade?)null);

        var result = await Handle(
            NewEvent(eventId: "EV-2", type: "trade_cancelled", reverses: "EV-9"));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }
}
