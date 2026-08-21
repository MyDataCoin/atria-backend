using Atria.Domain.Whitelist;

namespace Atria.Application.Whitelist.Dtos;

/// <summary>One row of the whitelist queue as the operator sees it.</summary>
/// <param name="Id">Request identifier — what mint-list assembly is given.</param>
/// <param name="InvestmentId">The purchase behind the request.</param>
/// <param name="InvestorId">The investor who applied.</param>
/// <param name="PropertyId">The issuance the shares belong to.</param>
/// <param name="PropertyName">Name of that issuance, for the table.</param>
/// <param name="TokenCount">Shares to mint.</param>
/// <param name="WalletAddress">Address to mint to; <c>null</c> means the request cannot be batched yet.</param>
/// <param name="Status">Where the request stands.</param>
/// <param name="RequestedAtUtc">When the investor pressed buy.</param>
/// <param name="ApprovedAtUtc">When an operator approved the application.</param>
/// <param name="MintListId">The batch holding the request, if any.</param>
/// <param name="MintListNumber">That batch's human-readable number.</param>
/// <param name="MintedAtUtc">When the batch was reported executed.</param>
/// <param name="ExclusionReason">Why the request left the queue unminted.</param>
public sealed record WhitelistEntryDto(
    Guid Id,
    Guid InvestmentId,
    Guid InvestorId,
    Guid PropertyId,
    string? PropertyName,
    long TokenCount,
    string? WalletAddress,
    WhitelistStatus Status,
    DateTime RequestedAtUtc,
    DateTime? ApprovedAtUtc,
    Guid? MintListId,
    string? MintListNumber,
    DateTime? MintedAtUtc,
    string? ExclusionReason);

/// <summary>Header of a mint list, without its lines. What the batch list shows.</summary>
/// <param name="Id">Batch identifier.</param>
/// <param name="Number">Human-readable batch number quoted to the exchange.</param>
/// <param name="PropertyId">The issuance the batch mints for.</param>
/// <param name="PropertyName">Name of that issuance.</param>
/// <param name="TokenContractAddress">The issuance's permissioned token contract at assembly time.</param>
/// <param name="TokenChain">Chain the contract lives on.</param>
/// <param name="Status">Draft / Sent / Executed / Cancelled.</param>
/// <param name="ItemCount">Number of lines.</param>
/// <param name="TotalTokens">Shares the batch mints in total.</param>
/// <param name="CreatedByUserId">Who assembled it.</param>
/// <param name="CreatedAtUtc">When it was assembled.</param>
/// <param name="SentAtUtc">When it went to the exchange.</param>
/// <param name="ExecutedAtUtc">When the exchange reported it minted.</param>
/// <param name="Note">The operator's note.</param>
/// <param name="CancellationReason">Why it was called off.</param>
public sealed record MintListDto(
    Guid Id,
    string Number,
    Guid PropertyId,
    string? PropertyName,
    string? TokenContractAddress,
    string? TokenChain,
    MintListStatus Status,
    int ItemCount,
    long TotalTokens,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    DateTime? ExecutedAtUtc,
    string? Note,
    string? CancellationReason);

/// <summary>One line of a batch: an address and the shares to mint to it.</summary>
/// <param name="WhitelistEntryId">The queued request the line came from.</param>
/// <param name="InvestmentId">The purchase behind it.</param>
/// <param name="InvestorId">The investor the shares go to.</param>
/// <param name="WalletAddress">Address to mint to, frozen at assembly.</param>
/// <param name="TokenCount">Shares to mint to that address.</param>
public sealed record MintListItemDto(
    Guid WhitelistEntryId,
    Guid InvestmentId,
    Guid InvestorId,
    string WalletAddress,
    long TokenCount);

/// <summary>A batch with its lines — what the exchange is handed, in JSON.</summary>
/// <param name="MintList">Batch header.</param>
/// <param name="Items">Lines, ordered by address as they were frozen.</param>
public sealed record MintListDetailDto(
    MintListDto MintList,
    IReadOnlyList<MintListItemDto> Items);

/// <summary>A batch rendered as a downloadable file.</summary>
/// <param name="FileName">Suggested file name, carrying the batch number.</param>
/// <param name="ContentType">MIME type of <paramref name="Content"/>.</param>
/// <param name="Content">File bytes.</param>
public sealed record MintListFileDto(string FileName, string ContentType, byte[] Content);
