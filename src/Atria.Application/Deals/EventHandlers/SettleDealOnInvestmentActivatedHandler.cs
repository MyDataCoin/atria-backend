using Atria.Application.Abstractions;
using Atria.Domain.Investments.Events;

namespace Atria.Application.Deals.EventHandlers;

/// <summary>
/// Closes a realtor's referral deal as Successful when an investment made through its link becomes
/// Active. The activated investment carries the referral token it was created with; if that token
/// still resolves to a redeemable deal, the deal is settled against this investment. Idempotent via
/// the processed-event ledger, so an at-least-once redelivery never settles twice.
/// Auto-discovered by the DI assembly scan.
/// </summary>
public sealed class SettleDealOnInvestmentActivatedHandler
    : IDomainEventHandler<InvestmentActivatedEvent>
{
    private readonly IInvestmentRepository _investments;
    private readonly IDealRepository _deals;
    private readonly IProcessedEventStore _processedEvents;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public SettleDealOnInvestmentActivatedHandler(
        IInvestmentRepository investments,
        IDealRepository deals,
        IProcessedEventStore processedEvents,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _investments = investments;
        _deals = deals;
        _processedEvents = processedEvents;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(InvestmentActivatedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processedEvents.IsProcessedAsync(key, ct))
            return;

        var investment = await _investments.GetByIdAsync(domainEvent.InvestmentId, ct);
        if (investment is not null && !string.IsNullOrWhiteSpace(investment.ReferralToken))
        {
            var deal = await _deals.GetByReferralTokenAsync(investment.ReferralToken, ct);
            // Only settle a deal that is still redeemable and points at the same property, so a link
            // that expired between purchase and activation doesn't retroactively pay out.
            if (deal is not null &&
                deal.PropertyId == investment.PropertyId &&
                deal.IsRedeemable(_clock.UtcNow))
            {
                deal.MarkSuccessful(investment.Id);
                _deals.Update(deal);
            }
        }

        await _processedEvents.MarkProcessedAsync(key, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
