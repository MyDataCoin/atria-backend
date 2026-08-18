using Atria.Domain.Common;

namespace Atria.Domain.Whitelist;

/// <summary>
/// One line of a mint list: an address and the number of shares to mint to it. Frozen when the list is
/// assembled, so the batch the exchange executes is the batch it was handed.
/// </summary>
public sealed class MintListItem : Entity
{
    public Guid MintListId { get; private set; }

    /// <summary>The queued request this line came from.</summary>
    public Guid WhitelistEntryId { get; private set; }

    /// <summary>The purchase behind the request, so a line traces back to an application.</summary>
    public Guid InvestmentId { get; private set; }

    public Guid InvestorId { get; private set; }

    /// <summary>Address to mint to, as it stood when the list was assembled.</summary>
    public string WalletAddress { get; private set; } = null!;

    public decimal TokenCount { get; private set; }

    private MintListItem() { }

    internal static MintListItem Create(
        Guid mintListId, Guid whitelistEntryId, Guid investmentId, Guid investorId,
        string walletAddress, decimal tokenCount)
        => new()
        {
            Id = Guid.NewGuid(),
            MintListId = mintListId,
            WhitelistEntryId = whitelistEntryId,
            InvestmentId = investmentId,
            InvestorId = investorId,
            WalletAddress = walletAddress,
            TokenCount = tokenCount
        };
}
