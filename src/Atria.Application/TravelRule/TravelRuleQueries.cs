using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.TravelRule;

namespace Atria.Application.TravelRule;

/// <summary>
/// A disclosure as the compliance queue shows it.
/// </summary>
/// <remarks>
/// The personal data is deliberately not here. This is a work queue — what it needs to show is which
/// transfers owe a disclosure and where each one stands, and a queue screen that also displays every
/// holder's document number turns a routine operational view into a data-protection problem. The
/// content itself is read one message at a time, by someone who needs it.
/// </remarks>
/// <param name="Id">The disclosure.</param>
/// <param name="PropertyId">The issue whose shares moved.</param>
/// <param name="InvestorId">The investor on our side.</param>
/// <param name="Direction">Whether the shares left us or arrived.</param>
/// <param name="Status">Where the disclosure stands.</param>
/// <param name="TokenCount">Shares transferred.</param>
/// <param name="Amount">Value of the transfer.</param>
/// <param name="Currency">Currency of the value.</param>
/// <param name="OriginatorAddress">Address the shares left.</param>
/// <param name="BeneficiaryAddress">Address the shares arrived at.</param>
/// <param name="CounterpartyVasp">The counterparty service provider, when known.</param>
/// <param name="HasBeneficiary">Whether the receiving person has been identified yet.</param>
/// <param name="TransactionHash">The transfer on chain.</param>
/// <param name="SentAtUtc">When it was delivered.</param>
/// <param name="FailureReason">Why it was refused or could not be delivered.</param>
/// <param name="CreatedAtUtc">When the obligation arose.</param>
public sealed record TravelRuleMessageDto(
    Guid Id,
    Guid PropertyId,
    Guid InvestorId,
    TravelRuleDirection Direction,
    TravelRuleStatus Status,
    long TokenCount,
    decimal Amount,
    string Currency,
    string OriginatorAddress,
    string BeneficiaryAddress,
    string? CounterpartyVasp,
    bool HasBeneficiary,
    string? TransactionHash,
    DateTime? SentAtUtc,
    string? FailureReason,
    DateTime CreatedAtUtc);

/// <summary>Disclosures still owed, oldest first.</summary>
public sealed record GetOutstandingTravelRuleMessagesQuery
    : IRequest<Result<IReadOnlyList<TravelRuleMessageDto>>>;

/// <summary>Disclosures recorded for one issue, newest first.</summary>
public sealed record GetTravelRuleMessagesByPropertyQuery(Guid PropertyId)
    : IRequest<Result<IReadOnlyList<TravelRuleMessageDto>>>;

/// <summary>The compliance queue reads.</summary>
public sealed class TravelRuleQueryHandlers
    : IRequestHandler<GetOutstandingTravelRuleMessagesQuery, Result<IReadOnlyList<TravelRuleMessageDto>>>,
      IRequestHandler<GetTravelRuleMessagesByPropertyQuery, Result<IReadOnlyList<TravelRuleMessageDto>>>
{
    private readonly ITravelRuleRepository _messages;

    public TravelRuleQueryHandlers(ITravelRuleRepository messages) => _messages = messages;

    public async Task<Result<IReadOnlyList<TravelRuleMessageDto>>> Handle(
        GetOutstandingTravelRuleMessagesQuery request, CancellationToken ct)
        => Result.Success<IReadOnlyList<TravelRuleMessageDto>>(
            (await _messages.ListOutstandingAsync(ct)).Select(ToDto).ToList());

    public async Task<Result<IReadOnlyList<TravelRuleMessageDto>>> Handle(
        GetTravelRuleMessagesByPropertyQuery request, CancellationToken ct)
        => Result.Success<IReadOnlyList<TravelRuleMessageDto>>(
            (await _messages.ListByPropertyAsync(request.PropertyId, ct)).Select(ToDto).ToList());

    private static TravelRuleMessageDto ToDto(TravelRuleMessage m)
        => new(
            m.Id, m.PropertyId, m.InvestorId, m.Direction, m.Status, m.TokenCount, m.Amount,
            m.Currency, m.OriginatorAddress, m.BeneficiaryAddress, m.CounterpartyVasp,
            !string.IsNullOrWhiteSpace(m.BeneficiaryName), m.TransactionHash, m.SentAtUtc,
            m.FailureReason, m.CreatedAtUtc);
}
