using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Kyc.Commands;

/// <summary>
/// Handles <see cref="LinkKycWalletCommand"/>: the investor links a wallet to their OWN
/// KYC profile. Rejects if no profile exists (404) or a wallet is already linked (409) —
/// the allocation address is not overwritten silently.
/// </summary>
public sealed class LinkKycWalletCommandHandler : IRequestHandler<LinkKycWalletCommand, Result>
{
    private readonly IKycRepository _kyc;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _uow;

    public LinkKycWalletCommandHandler(
        IKycRepository kyc,
        ICurrentUserService currentUser,
        IUnitOfWork uow)
    {
        _kyc = kyc;
        _currentUser = currentUser;
        _uow = uow;
    }

    public async Task<Result> Handle(LinkKycWalletCommand request, CancellationToken ct)
    {
        // Resource owner: the current investor acts on their OWN profile only.
        if (_currentUser.UserId is not { } userId)
            return Result.Failure(Error.Unauthorized("Kyc.Unauthorized", "Authentication required."));

        var profile = await _kyc.GetByUserIdAsync(userId, ct);
        if (profile is null)
            return Result.Failure(Error.NotFound("Kyc.NotFound", "No KYC profile exists for this user."));

        // The wallet feeds on-chain token allocation — don't silently replace an existing one.
        if (!string.IsNullOrEmpty(profile.WalletAddress))
            return Result.Failure(Error.Conflict(
                "Kyc.WalletAlreadyLinked", "A wallet is already linked to this KYC profile."));

        profile.LinkWallet(request.WalletAddress);
        _kyc.Update(profile);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
