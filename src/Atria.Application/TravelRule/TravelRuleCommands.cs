using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Common;
using Atria.Domain.TravelRule;

namespace Atria.Application.TravelRule;

/// <summary>Attempts delivery of every disclosure still owed. Returns how many went out.</summary>
public sealed record DeliverOutstandingTravelRuleMessagesCommand : IRequest<Result<int>>;

/// <summary>Fills in the beneficiary once the counterparty has identified them.</summary>
/// <param name="MessageId">The disclosure.</param>
/// <param name="BeneficiaryName">Who is receiving, as the counterparty verified them.</param>
/// <param name="CounterpartyVasp">The counterparty service provider, if now known.</param>
public sealed record SetTravelRuleBeneficiaryCommand(
    Guid MessageId, string BeneficiaryName, string? CounterpartyVasp) : IRequest<Result>;

/// <summary>
/// Works the queue of disclosures owed to counterparties.
/// </summary>
/// <remarks>
/// <para>
/// The payload is built here and frozen onto the message at the moment of delivery, not at assembly
/// time. What was disclosed has to be answerable years later, and a payload rebuilt from today's
/// identity file answers a different question than the one that was asked.
/// </para>
/// <para>
/// With no counterparty configured the run stops before touching anything. Otherwise every message
/// would collect an identical "no counterparty" failure on every pass, burying the ones that failed
/// for a reason somebody could act on.
/// </para>
/// </remarks>
public sealed class TravelRuleDeliveryHandlers
    : IRequestHandler<DeliverOutstandingTravelRuleMessagesCommand, Result<int>>,
      IRequestHandler<SetTravelRuleBeneficiaryCommand, Result>
{
    private readonly ITravelRuleRepository _messages;
    private readonly ITravelRuleGateway _gateway;
    private readonly ITravelRuleSettings _settings;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public TravelRuleDeliveryHandlers(
        ITravelRuleRepository messages,
        ITravelRuleGateway gateway,
        ITravelRuleSettings settings,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _messages = messages;
        _gateway = gateway;
        _settings = settings;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<int>> Handle(
        DeliverOutstandingTravelRuleMessagesCommand request, CancellationToken ct)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<int>(Error.Conflict(
                "travelRule.noCounterparty",
                "No counterparty service provider is configured; disclosures stay queued."));

        var outstanding = await _messages.ListOutstandingAsync(ct);
        var delivered = 0;

        foreach (var message in outstanding)
        {
            // Without a named counterparty there is no one to deliver to. That is not a failure of
            // ours to record repeatedly — it is a message waiting for the receiving side to be
            // identified.
            if (string.IsNullOrWhiteSpace(message.CounterpartyVasp))
                continue;

            var payload = TravelRulePayload.Build(
                message, _settings.OriginatingVaspName, _settings.OriginatingVaspId);

            var result = await _gateway.SendAsync(message.CounterpartyVasp, payload, ct);

            if (result.Delivered)
            {
                message.MarkSent(payload, result.CounterpartyReference, _clock.UtcNow);
                delivered++;
            }
            else
            {
                message.MarkFailed(result.FailureReason ?? "Delivery failed.");
            }

            _messages.Update(message);
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Success(delivered);
    }

    public async Task<Result> Handle(SetTravelRuleBeneficiaryCommand request, CancellationToken ct)
    {
        var message = await _messages.GetByIdAsync(request.MessageId, ct);
        if (message is null)
            return Result.Failure(Error.NotFound("travelRule.notFound", "Disclosure not found."));

        try
        {
            message.SetBeneficiary(request.BeneficiaryName, request.CounterpartyVasp);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Validation("travelRule.invalid", ex.Message));
        }

        _messages.Update(message);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
