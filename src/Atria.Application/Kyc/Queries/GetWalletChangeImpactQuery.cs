using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Kyc.Queries;

/// <summary>What replacing the current allocation address would leave behind.</summary>
/// <param name="CurrentWalletAddress">The address linked today; <c>null</c> when none is.</param>
/// <param name="StrandedTokenCount">
/// Shares that stay on the current address if it is replaced. They do NOT move: the platform holds
/// no key for the holder's wallet and cannot transfer them.
/// </param>
/// <param name="StrandedIssueCount">How many issuances those shares are spread across.</param>
/// <param name="HasPendingMintBatch">
/// A request naming the current address is already in a mint list. That row is a document the
/// exchange is acting on, so the address cannot move until the batch is executed or called off.
/// </param>
public sealed record WalletChangeImpactDto(
    string? CurrentWalletAddress,
    long StrandedTokenCount,
    int StrandedIssueCount,
    bool HasPendingMintBatch);

/// <summary>Asks what a wallet change would cost, before asking for a code.</summary>
public sealed record GetWalletChangeImpactQuery : IRequest<Result<WalletChangeImpactDto>>;

/// <summary>
/// Reads the register for the address about to be replaced.
/// </summary>
/// <remarks>
/// Shown before the change so the decision is informed rather than discovered afterwards: shares
/// already minted stay where they are, and an investor who does not know that would find their
/// portfolio empty and their dividends going to an address the platform no longer associates
/// with them.
/// </remarks>
public sealed class GetWalletChangeImpactQueryHandler
    : IRequestHandler<GetWalletChangeImpactQuery, Result<WalletChangeImpactDto>>
{
    private readonly IKycRepository _kyc;
    private readonly IHolderPositionRepository _positions;
    private readonly IWhitelistEntryRepository _entries;
    private readonly ICurrentUserService _currentUser;

    public GetWalletChangeImpactQueryHandler(
        IKycRepository kyc,
        IHolderPositionRepository positions,
        IWhitelistEntryRepository entries,
        ICurrentUserService currentUser)
    {
        _kyc = kyc;
        _positions = positions;
        _entries = entries;
        _currentUser = currentUser;
    }

    public async Task<Result<WalletChangeImpactDto>> Handle(
        GetWalletChangeImpactQuery request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<WalletChangeImpactDto>(
                Error.Unauthorized("Kyc.Unauthorized", "Authentication required."));

        var profile = await _kyc.GetByUserIdAsync(userId, ct);
        if (profile is null)
            return Result.Failure<WalletChangeImpactDto>(
                Error.NotFound("Kyc.NotFound", "No KYC profile exists for this user."));

        if (string.IsNullOrWhiteSpace(profile.WalletAddress))
            return Result.Success(new WalletChangeImpactDto(null, 0, 0, false));

        var current = profile.WalletAddress;
        var held = await _positions.GetByAddressAsync(current, ct);

        var entries = await _entries.ListByInvestorAsync(userId, ct);
        var batched = entries.Any(e =>
            e.Status == Domain.Whitelist.WhitelistStatus.Batched
            && string.Equals(e.WalletAddress, current, StringComparison.OrdinalIgnoreCase));

        return Result.Success(new WalletChangeImpactDto(
            current,
            held.Sum(p => p.TokenCount),
            held.Count,
            batched));
    }
}
