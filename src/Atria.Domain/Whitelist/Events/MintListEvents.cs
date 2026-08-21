using Atria.Domain.Common;

namespace Atria.Domain.Whitelist.Events;

/// <summary>
/// Raised when a mint list is handed to the exchange. The moment the platform stops being the only
/// party holding the batch, so it belongs in the audit trail rather than only in a status column.
/// </summary>
public sealed record MintListSentToExchangeEvent(
    Guid MintListId,
    string Number,
    Guid PropertyId,
    int ItemCount,
    long TotalTokens) : DomainEventBase;

/// <summary>Raised when the exchange reports a batch minted.</summary>
public sealed record MintListExecutedEvent(
    Guid MintListId,
    string Number,
    Guid PropertyId,
    int ItemCount,
    long TotalTokens) : DomainEventBase;

/// <summary>Raised when a mint list is called off; its requests return to the mintable pool.</summary>
public sealed record MintListCancelledEvent(
    Guid MintListId,
    string Number,
    Guid PropertyId,
    string Reason) : DomainEventBase;
