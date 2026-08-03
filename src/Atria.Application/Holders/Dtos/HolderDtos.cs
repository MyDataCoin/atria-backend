using Atria.Domain.Holders;

namespace Atria.Application.Holders.Dtos;

/// <summary>One line of the current holder register: an address and what it holds right now.</summary>
/// <param name="WalletAddress">The address the shares sit on — the record key alongside the property.</param>
/// <param name="TokenCount">Shares currently held on the address.</param>
/// <param name="InvestorId">Investor resolved from KYC; <c>null</c> means an unlinked address to investigate.</param>
/// <param name="IsAllowlisted">Allowlist state as of the last sync. A delisted address can still hold shares.</param>
/// <param name="Source">Where the count was last established (our records / chain).</param>
/// <param name="LastSyncedAtUtc">When the position was last reconciled with its source.</param>
public sealed record HolderPositionDto(
    string WalletAddress,
    long TokenCount,
    Guid? InvestorId,
    bool IsAllowlisted,
    HolderSource Source,
    DateTime LastSyncedAtUtc);

/// <summary>Header of a frozen snapshot, without its rows. What the snapshot list shows.</summary>
/// <param name="Id">Snapshot identifier.</param>
/// <param name="PropertyId">The issuance the snapshot covers.</param>
/// <param name="SnapshotAtUtc">The instant the register was cut.</param>
/// <param name="Purpose">Why the snapshot was taken (serialized by name).</param>
/// <param name="BlockNumber">Chain height the cut corresponds to; <c>null</c> until chain reading is wired.</param>
/// <param name="TotalTokens">Total shares across all rows — the denominator of every row's share.</param>
/// <param name="AddressCount">Number of holding addresses.</param>
/// <param name="CreatedByUserId">Who took the snapshot.</param>
/// <param name="CreatedAtUtc">When the snapshot record was created.</param>
public sealed record HolderSnapshotDto(
    Guid Id,
    Guid PropertyId,
    DateTime SnapshotAtUtc,
    SnapshotPurpose Purpose,
    long? BlockNumber,
    long TotalTokens,
    int AddressCount,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc);

/// <summary>One frozen row: what an address held at the cut and its share of the issuance.</summary>
/// <param name="WalletAddress">The holding address.</param>
/// <param name="TokenCount">Shares held at the cut.</param>
/// <param name="InvestorId">Investor behind the address at the cut, if resolved.</param>
/// <param name="Share">TokenCount / TotalTokens, rounded to <see cref="HolderSnapshot.ShareScale"/> places.</param>
public sealed record HolderSnapshotRowDto(
    string WalletAddress,
    long TokenCount,
    Guid? InvestorId,
    decimal Share);

/// <summary>A snapshot with its rows — the full frozen register as of the cut.</summary>
/// <param name="Snapshot">Snapshot header.</param>
/// <param name="Rows">Rows, ordered by address as they were frozen.</param>
public sealed record HolderSnapshotDetailDto(
    HolderSnapshotDto Snapshot,
    IReadOnlyList<HolderSnapshotRowDto> Rows);

/// <summary>A snapshot rendered as a downloadable file.</summary>
/// <param name="FileName">Suggested file name, carrying the property and the cut.</param>
/// <param name="ContentType">MIME type of <paramref name="Content"/>.</param>
/// <param name="Content">File bytes.</param>
public sealed record HolderSnapshotFileDto(string FileName, string ContentType, byte[] Content);
