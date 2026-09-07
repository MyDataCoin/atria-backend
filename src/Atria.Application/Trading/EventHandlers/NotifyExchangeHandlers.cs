using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Compliance.Events;
using Atria.Domain.Whitelist.Events;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Trading.EventHandlers;

/// <summary>
/// Tells the trading operator that a batch of shares was issued.
/// </summary>
/// <remarks>
/// The operator admits shares to trading on the strength of this, and judges what is in circulation
/// against it. We issue shares ourselves — the operator never mints — so an issuance they are not
/// told about is volume that exists on chain and not on their books.
/// </remarks>
public sealed class NotifyExchangeOnMintListExecutedHandler
    : IDomainEventHandler<MintListExecutedEvent>
{
    private readonly IExchangeNotifier _exchange;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<NotifyExchangeOnMintListExecutedHandler> _logger;

    public NotifyExchangeOnMintListExecutedHandler(
        IExchangeNotifier exchange,
        IProcessedEventStore processed,
        IUnitOfWork uow,
        ILogger<NotifyExchangeOnMintListExecutedHandler> logger)
    {
        _exchange = exchange;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    public async Task HandleAsync(MintListExecutedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        var payload = JsonSerializer.Serialize(new
        {
            notice = "issuance_executed",
            mint_list = domainEvent.Number,
            property_id = domainEvent.PropertyId,
            token_count = domainEvent.TotalTokens,
            holders = domainEvent.ItemCount
        });

        var delivered = await _exchange.NotifyAsync(
            new ExchangeNotice(
                ExchangeNoticeKind.IssuanceExecuted, domainEvent.PropertyId,
                domainEvent.Number, payload),
            ct);

        // Marked processed either way. Retrying delivery is the notifier's business, and re-raising
        // the notice on every redelivery of the domain event would flood the operator with duplicates
        // of an announcement that is only true once.
        await _processed.MarkProcessedAsync(key, ct);
        await _uow.SaveChangesAsync(ct);

        if (!delivered)
        {
            _logger.LogWarning(
                "Issuance {MintList} was not announced to the trading operator; the shares exist on "
                + "chain but their books will not show them.",
                domainEvent.Number);
        }
    }
}

/// <summary>
/// Tells the trading operator that a batch announced earlier was called off.
/// </summary>
/// <remarks>
/// Without this the operator keeps a volume on their books that we never issued. A batch can be
/// cancelled after it was sent — the counterpart to announcing it in the first place.
/// </remarks>
public sealed class NotifyExchangeOnMintListCancelledHandler
    : IDomainEventHandler<MintListCancelledEvent>
{
    private readonly IExchangeNotifier _exchange;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;

    public NotifyExchangeOnMintListCancelledHandler(
        IExchangeNotifier exchange, IProcessedEventStore processed, IUnitOfWork uow)
    {
        _exchange = exchange;
        _processed = processed;
        _uow = uow;
    }

    public async Task HandleAsync(MintListCancelledEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        var payload = JsonSerializer.Serialize(new
        {
            notice = "issuance_cancelled",
            mint_list = domainEvent.Number,
            property_id = domainEvent.PropertyId,
            reason = domainEvent.Reason
        });

        await _exchange.NotifyAsync(
            new ExchangeNotice(
                ExchangeNoticeKind.IssuanceCancelled, domainEvent.PropertyId,
                domainEvent.Number, payload),
            ct);

        await _processed.MarkProcessedAsync(key, ct);
        await _uow.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Tells the trading operator that an address was removed from the allowlist, so their open orders
/// against it can be pulled.
/// </summary>
/// <remarks>
/// <para>
/// This is the notice with real money behind it. The permissioned contract refuses a transfer to or
/// from an address that is not allowlisted, so an order left standing against a delisted address
/// produces a trade that cannot settle — matched on their side, rejected by the chain, and unwindable
/// only by hand.
/// </para>
/// <para>
/// Only removals are announced. An addition is not urgent in the same way: an address that has just
/// become tradable has no stale orders to pull, and the operator learns of it when it is used.
/// </para>
/// </remarks>
public sealed class NotifyExchangeOnAllowlistRemovalHandler
    : IDomainEventHandler<AllowlistUpdatedEvent>
{
    private readonly IExchangeNotifier _exchange;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<NotifyExchangeOnAllowlistRemovalHandler> _logger;

    public NotifyExchangeOnAllowlistRemovalHandler(
        IExchangeNotifier exchange,
        IProcessedEventStore processed,
        IUnitOfWork uow,
        ILogger<NotifyExchangeOnAllowlistRemovalHandler> logger)
    {
        _exchange = exchange;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    public async Task HandleAsync(AllowlistUpdatedEvent domainEvent, CancellationToken ct)
    {
        if (domainEvent.Added)
            return;

        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        // The allowlist is kept per investor, not per issue, so the notice names the address and
        // leaves the operator to pull its orders across every issue they list it in.
        var payload = JsonSerializer.Serialize(new
        {
            notice = "address_delisted",
            wallet_address = domainEvent.WalletAddress,
            action = "cancel_open_orders"
        });

        var delivered = await _exchange.NotifyAsync(
            new ExchangeNotice(
                ExchangeNoticeKind.AddressDelisted, Guid.Empty,
                domainEvent.WalletAddress, payload),
            ct);

        await _processed.MarkProcessedAsync(key, ct);
        await _uow.SaveChangesAsync(ct);

        if (!delivered)
        {
            // Loud: a trade matched against this address from here on cannot settle on chain.
            _logger.LogWarning(
                "Address {Wallet} left the allowlist but the trading operator was not told; any "
                + "order still standing against it will match and fail to settle.",
                domainEvent.WalletAddress);
        }
    }
}
