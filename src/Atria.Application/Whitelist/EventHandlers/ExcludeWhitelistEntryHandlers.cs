using Atria.Application.Abstractions;
using Atria.Domain.Investments.Events;
using Atria.Domain.Whitelist;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Whitelist.EventHandlers;

/// <summary>
/// Shared body for the four ways a purchase can leave the queue without being minted. Each event gets
/// its own handler — that is how they are discovered and how exactly-once is keyed — but the work is
/// the same: find the request and drop it out of the queue with the reason it left.
/// </summary>
/// <remarks>
/// A request already minted is left as it is. Withdrawal in particular arrives after the shares exist
/// on chain; taking them back is a burn, a separate operation with its own record, and quietly marking
/// the request excluded here would claim shares were never minted when they were.
/// </remarks>
public abstract class ExcludeWhitelistEntryHandlerBase
{
    private readonly IWhitelistEntryRepository _entries;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger _logger;

    protected ExcludeWhitelistEntryHandlerBase(
        IWhitelistEntryRepository entries, IProcessedEventStore processed, IUnitOfWork uow, ILogger logger)
    {
        _entries = entries;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    protected async Task ExcludeAsync(Guid eventId, Guid investmentId, string reason, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, eventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        var entry = await _entries.GetByInvestmentAsync(investmentId, ct);
        if (entry is null)
        {
            _logger.LogWarning(
                "No whitelist request for investment {InvestmentId}; nothing to exclude.", investmentId);
        }
        else if (entry.Status == WhitelistStatus.Minted)
        {
            _logger.LogWarning(
                "Whitelist request {EntryId} for investment {InvestmentId} is already minted; "
                + "leaving it in place ({Reason}) — reversing minted shares is a burn.",
                entry.Id, investmentId, reason);
        }
        else
        {
            entry.Exclude(reason);
            _entries.Update(entry);
        }

        await _processed.MarkProcessedAsync(key, ct);
        await _uow.SaveChangesAsync(ct);
    }
}

/// <summary>Drops a request out of the queue when the operator declines the application.</summary>
public sealed class ExcludeWhitelistEntryOnInvestmentRejectedHandler
    : ExcludeWhitelistEntryHandlerBase, IDomainEventHandler<InvestmentRejectedEvent>
{
    public ExcludeWhitelistEntryOnInvestmentRejectedHandler(
        IWhitelistEntryRepository entries, IProcessedEventStore processed, IUnitOfWork uow,
        ILogger<ExcludeWhitelistEntryOnInvestmentRejectedHandler> logger)
        : base(entries, processed, uow, logger) { }

    public Task HandleAsync(InvestmentRejectedEvent domainEvent, CancellationToken ct)
        => ExcludeAsync(domainEvent.EventId, domainEvent.InvestmentId,
            $"Заявка отклонена оператором: {domainEvent.Reason}", ct);
}

/// <summary>Drops a request out of the queue when the investor cancels the application.</summary>
public sealed class ExcludeWhitelistEntryOnInvestmentCancelledHandler
    : ExcludeWhitelistEntryHandlerBase, IDomainEventHandler<InvestmentCancelledEvent>
{
    public ExcludeWhitelistEntryOnInvestmentCancelledHandler(
        IWhitelistEntryRepository entries, IProcessedEventStore processed, IUnitOfWork uow,
        ILogger<ExcludeWhitelistEntryOnInvestmentCancelledHandler> logger)
        : base(entries, processed, uow, logger) { }

    public Task HandleAsync(InvestmentCancelledEvent domainEvent, CancellationToken ct)
        => ExcludeAsync(domainEvent.EventId, domainEvent.InvestmentId, "Инвестор отменил заявку", ct);
}

/// <summary>Drops a request out of the queue when its reservation lapses unapproved.</summary>
public sealed class ExcludeWhitelistEntryOnInvestmentExpiredHandler
    : ExcludeWhitelistEntryHandlerBase, IDomainEventHandler<InvestmentExpiredEvent>
{
    public ExcludeWhitelistEntryOnInvestmentExpiredHandler(
        IWhitelistEntryRepository entries, IProcessedEventStore processed, IUnitOfWork uow,
        ILogger<ExcludeWhitelistEntryOnInvestmentExpiredHandler> logger)
        : base(entries, processed, uow, logger) { }

    public Task HandleAsync(InvestmentExpiredEvent domainEvent, CancellationToken ct)
        => ExcludeAsync(domainEvent.EventId, domainEvent.InvestmentId, "Срок брони истёк без одобрения", ct);
}

/// <summary>Drops a request out of the queue when an operator voids the application.</summary>
public sealed class ExcludeWhitelistEntryOnInvestmentAnnulledHandler
    : ExcludeWhitelistEntryHandlerBase, IDomainEventHandler<InvestmentAnnulledEvent>
{
    public ExcludeWhitelistEntryOnInvestmentAnnulledHandler(
        IWhitelistEntryRepository entries, IProcessedEventStore processed, IUnitOfWork uow,
        ILogger<ExcludeWhitelistEntryOnInvestmentAnnulledHandler> logger)
        : base(entries, processed, uow, logger) { }

    public Task HandleAsync(InvestmentAnnulledEvent domainEvent, CancellationToken ct)
        => ExcludeAsync(domainEvent.EventId, domainEvent.InvestmentId,
            $"Заявка аннулирована оператором: {domainEvent.Reason}", ct);
}

/// <summary>
/// Drops a request out of the queue when the holder exercises the 14-day right of withdrawal, provided
/// it had not been minted yet.
/// </summary>
public sealed class ExcludeWhitelistEntryOnInvestmentWithdrawnHandler
    : ExcludeWhitelistEntryHandlerBase, IDomainEventHandler<InvestmentWithdrawnEvent>
{
    public ExcludeWhitelistEntryOnInvestmentWithdrawnHandler(
        IWhitelistEntryRepository entries, IProcessedEventStore processed, IUnitOfWork uow,
        ILogger<ExcludeWhitelistEntryOnInvestmentWithdrawnHandler> logger)
        : base(entries, processed, uow, logger) { }

    public Task HandleAsync(InvestmentWithdrawnEvent domainEvent, CancellationToken ct)
        => ExcludeAsync(domainEvent.EventId, domainEvent.InvestmentId,
            "Инвестор воспользовался правом отказа (14 дней)", ct);
}
