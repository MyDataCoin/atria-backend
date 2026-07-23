using Atria.Domain.Common;

namespace Atria.Domain.Holders;

/// <summary>
/// The current holdings of a single wallet in a single issuance. Keyed by (PropertyId, WalletAddress),
/// not by investor: shares live on addresses and move address-to-address, so the wallet is the unit of
/// record and the investor is an attribute resolved from KYC. A mutable projection: it is kept up to
/// date from our records now and reconciled against the chain later (see <see cref="Source"/>). For the
/// frozen, historical view see <see cref="HolderSnapshot"/>.
/// </summary>
public sealed class HolderPosition : AggregateRoot
{
    /// <summary>The issuance these shares belong to.</summary>
    public Guid PropertyId { get; private set; }

    /// <summary>The wallet address that holds the shares — the record key alongside <see cref="PropertyId"/>.</summary>
    public string WalletAddress { get; private set; } = null!;

    /// <summary>Number of (indivisible) shares currently held on the address.</summary>
    public long TokenCount { get; private set; }

    /// <summary>
    /// The investor behind the address, resolved via <c>KycProfile.WalletAddress</c>. Null when no link
    /// is found — that is a signal for investigation, not a valid steady state, since every holder is a
    /// wallet we allowlisted for a known investor.
    /// </summary>
    public Guid? InvestorId { get; private set; }

    /// <summary>
    /// Whether the address is on the allowlist as of the last sync. An address can drop off the
    /// allowlist yet keep its shares (e.g. the investor was blocked — we hold no keys and cannot claw
    /// tokens back), so this can be false on a position with a non-zero balance; such holders still
    /// belong in the register, flagged.
    /// </summary>
    public bool IsAllowlisted { get; private set; }

    /// <summary>When this position was last reconciled with its <see cref="Source"/>.</summary>
    public DateTime LastSyncedAtUtc { get; private set; }

    /// <summary>Where <see cref="TokenCount"/> was last established.</summary>
    public HolderSource Source { get; private set; }

    private HolderPosition() { }

    /// <summary>Creates a position for a wallet in an issuance.</summary>
    public static HolderPosition Create(
        Guid propertyId, string walletAddress, long tokenCount, Guid? investorId,
        bool isAllowlisted, HolderSource source, DateTime syncedAtUtc)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new DomainException("Wallet address is required.");
        if (tokenCount < 0)
            throw new DomainException("Token count cannot be negative.");

        return new HolderPosition
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            WalletAddress = walletAddress,
            TokenCount = tokenCount,
            InvestorId = investorId,
            IsAllowlisted = isAllowlisted,
            Source = source,
            LastSyncedAtUtc = syncedAtUtc
        };
    }

    /// <summary>
    /// Adds shares acquired through our own records (an activated application), marking the source
    /// accordingly. Used to project the registry from our data before chain reading exists; the caller
    /// guards against double-applying the same event, so this is a plain additive adjustment.
    /// </summary>
    public void Increase(long count, DateTime syncedAtUtc)
    {
        if (count <= 0)
            throw new DomainException("Increase count must be positive.");

        TokenCount += count;
        Source = HolderSource.OurRecords;
        LastSyncedAtUtc = syncedAtUtc;
    }

    /// <summary>
    /// Reconciles the position with an observed value: sets the token count, its origin, and the sync
    /// time. Idempotent — applying the same observation twice yields the same state.
    /// </summary>
    public void Sync(long tokenCount, HolderSource source, DateTime syncedAtUtc)
    {
        if (tokenCount < 0)
            throw new DomainException("Token count cannot be negative.");

        TokenCount = tokenCount;
        Source = source;
        LastSyncedAtUtc = syncedAtUtc;
    }

    /// <summary>Records whether the address is currently on the allowlist.</summary>
    public void SetAllowlisted(bool isAllowlisted) => IsAllowlisted = isAllowlisted;

    /// <summary>Attaches (or clears) the investor resolved for this address.</summary>
    public void LinkInvestor(Guid? investorId) => InvestorId = investorId;
}
