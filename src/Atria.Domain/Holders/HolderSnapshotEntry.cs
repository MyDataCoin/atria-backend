namespace Atria.Domain.Holders;

/// <summary>
/// Input line for building a <see cref="HolderSnapshot"/>: one address and the shares it holds at the
/// cut, plus the resolved investor. The snapshot factory computes each address's share from these.
/// </summary>
public readonly record struct HolderSnapshotEntry(string WalletAddress, long TokenCount, Guid? InvestorId);
