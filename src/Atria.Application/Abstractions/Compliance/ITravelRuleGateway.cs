namespace Atria.Application.Abstractions;

/// <summary>Outcome of handing a travel-rule message to the counterparty.</summary>
/// <param name="Delivered">Whether the counterparty actually took it.</param>
/// <param name="CounterpartyReference">Their id for the message, when they gave one.</param>
/// <param name="FailureReason">Why it was not delivered. Set exactly when <paramref name="Delivered"/> is false.</param>
public sealed record TravelRuleDeliveryResult(
    bool Delivered, string? CounterpartyReference, string? FailureReason)
{
    public static TravelRuleDeliveryResult Success(string? reference) => new(true, reference, null);
    public static TravelRuleDeliveryResult Failure(string reason) => new(false, null, reason);
}

/// <summary>
/// Hands originator/beneficiary information to the counterparty service provider.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a port with no implementation that transmits anything yet: which provider runs
/// secondary trading is an open decision, and every travel-rule network (TRP, TRISA, Notabene and
/// the rest) is a different protocol behind the same obligation. The obligation itself does not wait
/// for that decision — messages are assembled and queued regardless, so the day a counterparty is
/// chosen the backlog is deliverable rather than lost.
/// </para>
/// <para>
/// An implementation must report failure rather than throw for a counterparty that refuses or is
/// unreachable: an undelivered obligation has to stay visible in the queue, and an exception that
/// unwinds the transaction would erase the record of the attempt.
/// </para>
/// </remarks>
public interface ITravelRuleGateway
{
    /// <summary>Whether a counterparty is configured at all.</summary>
    /// <remarks>
    /// Read before attempting delivery so the queue is not filled with identical "no counterparty"
    /// failures on every pass, which would bury the messages that failed for a real reason.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>Delivers one assembled payload to the counterparty named in it.</summary>
    Task<TravelRuleDeliveryResult> SendAsync(
        string counterpartyVasp, string payload, CancellationToken ct);
}
