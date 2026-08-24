using Atria.Domain.Common;
using Atria.Domain.Compliance;

namespace Atria.Domain.Whitelist;

/// <summary>
/// One investor's purchase request as it sits in the whitelist queue: the address the shares are to be
/// minted to, how many, and how far along the request is.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Atria.Domain.Investments.Investment"/> remains the record of the purchase — this is
/// the operating queue built on top of it, created the moment the investor presses buy and driven
/// forward by the investment's own events. It carries what the investment does not: the wallet the
/// shares go to, and which mint list the request was handed to the exchange in.
/// </para>
/// <para>
/// The wallet is copied here rather than read from the compliance profile at mint time on purpose. An
/// investor may change their address; a list already sent to the exchange must keep naming the address
/// it was sent with, or the batch that comes back will not match the batch that went out.
/// </para>
/// </remarks>
public sealed class WhitelistEntry : AggregateRoot
{
    /// <summary>The purchase this request stands for. One entry per investment.</summary>
    public Guid InvestmentId { get; private set; }

    public Guid InvestorId { get; private set; }
    public Guid PropertyId { get; private set; }

    /// <summary>Shares to mint — the application's token count, snapshotted at request time.</summary>
    public long TokenCount { get; private set; }

    /// <summary>
    /// The address the shares are to be minted to, as known when the request was queued. Null when the
    /// investor has no wallet on their compliance profile yet: the request is still tracked, it simply
    /// cannot be batched until an address exists.
    /// </summary>
    public string? WalletAddress { get; private set; }

    public WhitelistStatus Status { get; private set; }

    /// <summary>When the investor submitted the purchase.</summary>
    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>When an operator approved the application, so the request became mintable.</summary>
    public DateTime? ApprovedAtUtc { get; private set; }

    /// <summary>The mint list this request was put into; null while it is not in one.</summary>
    public Guid? MintListId { get; private set; }

    /// <summary>When the batch carrying this request was reported executed.</summary>
    public DateTime? MintedAtUtc { get; private set; }

    /// <summary>Why the request left the queue unminted. Set on exclusion, kept afterwards.</summary>
    public string? ExclusionReason { get; private set; }

    /// <summary>True when the request can go into a mint list: approved, still free, and with an address.</summary>
    public bool IsMintable => Status == WhitelistStatus.Ready && !string.IsNullOrWhiteSpace(WalletAddress);

    private WhitelistEntry() { }

    /// <summary>Queues a freshly submitted purchase request, awaiting the operator's decision.</summary>
    public static WhitelistEntry Queue(
        Guid investmentId, Guid investorId, Guid propertyId, long tokenCount,
        string? walletAddress, DateTime requestedAtUtc)
    {
        if (investmentId == Guid.Empty)
            throw new DomainException("InvestmentId is required.");
        if (investorId == Guid.Empty)
            throw new DomainException("InvestorId is required.");
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");
        if (tokenCount <= 0)
            throw new DomainException("A whitelist request must be for a positive number of shares.");
        if (walletAddress is not null && !Compliance.WalletAddress.IsValid(walletAddress))
            throw new DomainException("Invalid EVM wallet address.");

        return new WhitelistEntry
        {
            Id = Guid.NewGuid(),
            InvestmentId = investmentId,
            InvestorId = investorId,
            PropertyId = propertyId,
            TokenCount = tokenCount,
            WalletAddress = walletAddress,
            Status = WhitelistStatus.Pending,
            RequestedAtUtc = requestedAtUtc
        };
    }

    /// <summary>
    /// Records the address the shares are to be minted to. Refused once the request is in a list: the
    /// exchange is holding a row that names the old address.
    /// </summary>
    public void SetWallet(string walletAddress)
    {
        if (!Compliance.WalletAddress.IsValid(walletAddress))
            throw new DomainException("Invalid EVM wallet address.");
        if (Status is WhitelistStatus.Batched or WhitelistStatus.Minted)
            throw new DomainException(
                "The address of a request that is already in a mint list cannot be changed.");

        WalletAddress = walletAddress;
    }

    /// <summary>Pending -> Ready: the operator approved the application, so the request may be minted.</summary>
    public void MarkReady(DateTime approvedAtUtc)
    {
        if (Status != WhitelistStatus.Pending)
            throw new InvalidStateTransitionException(
                "Only a request awaiting a decision can become mintable.");

        Status = WhitelistStatus.Ready;
        ApprovedAtUtc = approvedAtUtc;
    }

    /// <summary>Ready -> Batched: the request was placed into a mint list.</summary>
    public void IncludeIn(Guid mintListId)
    {
        if (mintListId == Guid.Empty)
            throw new DomainException("MintListId is required.");
        if (Status != WhitelistStatus.Ready)
            throw new InvalidStateTransitionException(
                "Only a mintable request can be placed into a mint list.");
        if (string.IsNullOrWhiteSpace(WalletAddress))
            throw new DomainException("A request without a wallet address cannot be minted.");

        Status = WhitelistStatus.Batched;
        MintListId = mintListId;
    }

    /// <summary>Batched -> Ready: the list was called off, so the request is mintable again.</summary>
    public void ReleaseFromMintList()
    {
        if (Status != WhitelistStatus.Batched)
            throw new InvalidStateTransitionException(
                "Only a request held in a mint list can be released from one.");

        Status = WhitelistStatus.Ready;
        MintListId = null;
    }

    /// <summary>
    /// Ready|Batched -> Minted: the shares now exist on chain. Reached from <see cref="WhitelistStatus.Ready"/>
    /// because the platform mints an approved application itself — the request never has to pass through a
    /// batch to become real. Batched is still accepted for a request that was handed to the exchange.
    /// Idempotent for an already-minted request: a confirmation the platform sees twice must not throw,
    /// or a retried allocation would fail after the shares were issued.
    /// </summary>
    public void MarkMinted(DateTime mintedAtUtc)
    {
        if (Status == WhitelistStatus.Minted)
            return;
        if (Status is not (WhitelistStatus.Ready or WhitelistStatus.Batched))
            throw new InvalidStateTransitionException(
                "Only a mintable request, or one handed over in a mint list, can be marked minted.");

        Status = WhitelistStatus.Minted;
        MintedAtUtc = mintedAtUtc;
    }

    /// <summary>
    /// Drops the request out of the queue — the application was rejected, cancelled, expired, or
    /// withdrawn. Minted shares exist on chain and cannot be un-queued; reversing those is a burn,
    /// which is a different operation entirely.
    /// </summary>
    public void Exclude(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("An exclusion reason is required.");
        if (Status == WhitelistStatus.Minted)
            throw new InvalidStateTransitionException(
                "A minted request cannot be excluded — the shares already exist on chain.");
        if (Status == WhitelistStatus.Excluded)
            return;

        Status = WhitelistStatus.Excluded;
        MintListId = null;
        ExclusionReason = reason;
    }
}
