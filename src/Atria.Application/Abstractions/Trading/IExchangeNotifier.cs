namespace Atria.Application.Abstractions;

/// <summary>What we tell the trading operator about, and why it cannot wait.</summary>
public enum ExchangeNoticeKind
{
    /// <summary>
    /// A batch of shares was issued. The operator needs the volume to admit shares to trading and to
    /// know what is in circulation.
    /// </summary>
    IssuanceExecuted = 0,

    /// <summary>
    /// A batch announced earlier was called off. Without this, a volume we told them about stays on
    /// their books forever.
    /// </summary>
    IssuanceCancelled = 1,

    /// <summary>
    /// An address was removed from the allowlist. Their open orders for it must be pulled: the
    /// contract will reject the transfer, so a trade against it cannot settle.
    /// </summary>
    AddressDelisted = 2,

    /// <summary>
    /// An investor moved to a new address. Orders standing against the old one must be pulled, since
    /// a trade is bound to an address and not to a person.
    /// </summary>
    AddressChanged = 3
}

/// <summary>One thing we owe the trading operator.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="PropertyId">The issue it concerns.</param>
/// <param name="Reference">Our identifier for the thing — a batch number, or an address.</param>
/// <param name="Payload">The notice as JSON, ready to hand over unchanged.</param>
public sealed record ExchangeNotice(
    ExchangeNoticeKind Kind, Guid PropertyId, string Reference, string Payload);

/// <summary>Delivers notices to the trading operator.</summary>
/// <remarks>
/// The obligations run both ways. We require the operator to push trade cards, to retry them, and to
/// pull orders when an address stops being tradable — but the last of those is only possible if we
/// tell them, and an allowlist removal or an address change is ours to report. This port exists so
/// that half of the contract is written down and testable now, while the transport is still an open
/// question with the operator.
/// </remarks>
public interface IExchangeNotifier
{
    /// <summary>Whether an operator endpoint is actually configured.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Hands one notice over. Implementations must not report success when nothing was delivered:
    /// the whole value of the notice is that someone acted on it.
    /// </summary>
    Task<bool> NotifyAsync(ExchangeNotice notice, CancellationToken ct);
}
