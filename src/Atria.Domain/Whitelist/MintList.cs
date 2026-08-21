using Atria.Domain.Common;
using Atria.Domain.Whitelist.Events;

namespace Atria.Domain.Whitelist;

/// <summary>
/// A batch of whitelisted purchase requests for one issuance, assembled to be handed to the exchange
/// for minting.
/// </summary>
/// <remarks>
/// <para>
/// One list, one issuance: shares are minted on the issuance's own permissioned contract, so a batch
/// spanning two issuances has no single contract to be executed against.
/// </para>
/// <para>
/// The lines are frozen at assembly. Once a list is sent, the exchange is holding a document; changing
/// what it says here would leave the two sides minting different things. A batch that turns out wrong
/// is called off and rebuilt, never edited.
/// </para>
/// </remarks>
public sealed class MintList : AggregateRoot
{
    /// <summary>Human-readable batch number, e.g. <c>ML-2026-0007</c>. What the exchange is told to quote.</summary>
    public string Number { get; private set; } = null!;

    public Guid PropertyId { get; private set; }

    /// <summary>The issuance's permissioned token contract, copied here so the file explains itself.</summary>
    public string? TokenContractAddress { get; private set; }

    /// <summary>The chain the contract lives on, copied at assembly.</summary>
    public string? TokenChain { get; private set; }

    public MintListStatus Status { get; private set; }

    /// <summary>Shares across all lines — what the batch mints in total.</summary>
    public long TotalTokens { get; private set; }

    /// <summary>Number of lines in the batch.</summary>
    public int ItemCount { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? ExecutedAtUtc { get; private set; }

    /// <summary>What this batch is, in the operator's words. Carried into the audit trail.</summary>
    public string? Note { get; private set; }

    /// <summary>Why the batch was called off, when it was.</summary>
    public string? CancellationReason { get; private set; }

    private readonly List<MintListItem> _items = new();
    public IReadOnlyCollection<MintListItem> Items => _items.AsReadOnly();

    private MintList() { }

    /// <summary>
    /// Assembles a batch from mintable requests. Every request must be approved, free of another list,
    /// carry an address, and belong to <paramref name="propertyId"/>; anything else would produce a
    /// line the exchange cannot execute. Lines are ordered by address so the same batch always renders
    /// the same file.
    /// </summary>
    public static MintList Create(
        string number, Guid propertyId, string? tokenContractAddress, string? tokenChain,
        Guid createdByUserId, string? note, IEnumerable<WhitelistEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (string.IsNullOrWhiteSpace(number))
            throw new DomainException("A mint list number is required.");
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");
        if (createdByUserId == Guid.Empty)
            throw new DomainException("CreatedByUserId is required.");

        var requests = entries
            .OrderBy(e => e.WalletAddress, StringComparer.Ordinal)
            .ThenBy(e => e.RequestedAtUtc)
            .ToList();

        if (requests.Count == 0)
            throw new DomainException("A mint list needs at least one request.");
        if (requests.Any(e => e.PropertyId != propertyId))
            throw new DomainException("Every request in a mint list must belong to the same issuance.");
        if (requests.Any(e => !e.IsMintable))
            throw new DomainException(
                "Every request in a mint list must be approved, free of another list, and carry a wallet address.");

        var list = new MintList
        {
            Id = Guid.NewGuid(),
            Number = number,
            PropertyId = propertyId,
            TokenContractAddress = tokenContractAddress,
            TokenChain = tokenChain,
            Status = MintListStatus.Draft,
            CreatedByUserId = createdByUserId,
            Note = note
        };

        foreach (var request in requests)
            list._items.Add(MintListItem.Create(
                list.Id, request.Id, request.InvestmentId, request.InvestorId,
                request.WalletAddress!, request.TokenCount));

        list.ItemCount = list._items.Count;
        list.TotalTokens = list._items.Sum(i => i.TokenCount);

        return list;
    }

    /// <summary>Draft -> Sent: the batch went to the exchange.</summary>
    public void MarkSent(DateTime nowUtc)
    {
        if (Status != MintListStatus.Draft)
            throw new InvalidStateTransitionException("Only a draft mint list can be sent to the exchange.");

        Status = MintListStatus.Sent;
        SentAtUtc = nowUtc;
        RaiseEvent(new MintListSentToExchangeEvent(Id, Number, PropertyId, ItemCount, TotalTokens));
    }

    /// <summary>
    /// Sent -> Executed: the exchange reports the batch minted. Only a sent batch can be executed —
    /// a draft the exchange never received cannot have been acted on.
    /// </summary>
    public void MarkExecuted(DateTime nowUtc)
    {
        if (Status != MintListStatus.Sent)
            throw new InvalidStateTransitionException(
                "Only a mint list already handed to the exchange can be marked executed.");

        Status = MintListStatus.Executed;
        ExecutedAtUtc = nowUtc;
        RaiseEvent(new MintListExecutedEvent(Id, Number, PropertyId, ItemCount, TotalTokens));
    }

    /// <summary>
    /// Calls the batch off and returns its requests to the mintable pool. Allowed on a sent list too —
    /// an exchange can decline a batch — but never on an executed one: those shares exist.
    /// </summary>
    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A cancellation reason is required.");
        if (Status is MintListStatus.Executed or MintListStatus.Cancelled)
            throw new InvalidStateTransitionException("This mint list is already closed.");

        Status = MintListStatus.Cancelled;
        CancellationReason = reason;
        RaiseEvent(new MintListCancelledEvent(Id, Number, PropertyId, reason));
    }
}
