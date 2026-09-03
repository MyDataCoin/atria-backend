using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Common;
using Atria.Domain.Whitelist;

namespace Atria.Application.Kyc.Commands;

/// <summary>
/// Asks for the SMS code that authorises moving the allocation address. Sends the code to the
/// investor's own registered phone; the new address is supplied at confirmation, not here.
/// </summary>
public sealed record RequestKycWalletChangeCommand : IRequest<Result>;

/// <summary>
/// Moves the investor's allocation address to <paramref name="WalletAddress"/>, authorised by the
/// SMS code from <see cref="RequestKycWalletChangeCommand"/>.
/// </summary>
/// <param name="WalletAddress">The new EVM address.</param>
/// <param name="Code">The SMS code just sent to the investor's registered phone.</param>
/// <param name="AcknowledgeStrandedShares">
/// The holder states they understand that shares already minted to the current address stay there.
/// Required only when there ARE such shares; the platform holds no key and cannot move them, so the
/// holder must transfer them themselves.
/// </param>
public sealed record ConfirmKycWalletChangeCommand(
    string WalletAddress, string Code, bool AcknowledgeStrandedShares = false)
    : IRequest<Result>;

/// <summary>
/// Sends the authorisation code for a wallet change.
/// </summary>
/// <remarks>
/// The code goes to the phone on the account, never to a number supplied in the request: the point
/// of the second factor is that it reaches the real holder, and a destination the caller chooses
/// proves nothing. Whether the change is even permitted is re-checked at confirmation — sending a
/// code to someone who cannot move their wallet only wastes an SMS, letting the move through would
/// be the real failure.
/// </remarks>
public sealed class RequestKycWalletChangeCommandHandler
    : IRequestHandler<RequestKycWalletChangeCommand, Result>
{
    private readonly IKycRepository _kyc;
    private readonly IUserRepository _users;
    private readonly IOtpService _otp;
    private readonly ICurrentUserService _currentUser;

    public RequestKycWalletChangeCommandHandler(
        IKycRepository kyc, IUserRepository users, IOtpService otp, ICurrentUserService currentUser)
    {
        _kyc = kyc;
        _users = users;
        _otp = otp;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(RequestKycWalletChangeCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure(Error.Unauthorized("Kyc.Unauthorized", "Authentication required."));

        var profile = await _kyc.GetByUserIdAsync(userId, ct);
        if (profile is null)
            return Result.Failure(Error.NotFound("Kyc.NotFound", "No KYC profile exists for this user."));
        if (string.IsNullOrWhiteSpace(profile.WalletAddress))
            return Result.Failure(Error.Conflict(
                "Kyc.NoWalletLinked", "No wallet is linked yet — link one instead of changing it."));

        var user = await _users.GetByIdAsync(userId, ct);
        if (user?.PhoneNumber is not { } phone || string.IsNullOrWhiteSpace(phone))
            return Result.Failure(Error.Conflict(
                "Kyc.NoPhone", "This account has no phone number to send a code to."));

        return await _otp.RequestAsync(phone, ipAddress: null, ct);
    }
}

/// <summary>
/// Verifies the code and moves the address.
/// </summary>
/// <remarks>
/// <para>
/// Minted shares do not follow the holder — the platform holds no key for their wallet and cannot
/// transfer them — so a change that would strand shares needs the holder to say, explicitly, that
/// they know. Refusing outright would trap someone who has lost access to the old key and has every
/// reason to move on; changing silently would leave them with an empty portfolio and dividends
/// going to an address we no longer associate with them.
/// </para>
/// <para>
/// A batch already handed to the exchange is the one case with no acknowledgement: that row names
/// the old address on a document someone else is acting on right now, and the mismatch is not the
/// holder's to accept. It clears when the batch executes or is called off.
/// </para>
/// <para>
/// Requests that are merely queued DO follow — the aggregate rewrites the ones it is allowed to
/// rewrite (see <see cref="WhitelistEntry.SetWallet"/>) — so an investor who corrects a typo before
/// anything is batched does not have to reapply.
/// </para>
/// </remarks>
public sealed class ConfirmKycWalletChangeCommandHandler
    : IRequestHandler<ConfirmKycWalletChangeCommand, Result>
{
    private readonly IKycRepository _kyc;
    private readonly IUserRepository _users;
    private readonly IWhitelistEntryRepository _entries;
    private readonly IHolderPositionRepository _positions;
    private readonly IOtpService _otp;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _uow;

    public ConfirmKycWalletChangeCommandHandler(
        IKycRepository kyc,
        IUserRepository users,
        IWhitelistEntryRepository entries,
        IHolderPositionRepository positions,
        IOtpService otp,
        ICurrentUserService currentUser,
        IUnitOfWork uow)
    {
        _kyc = kyc;
        _users = users;
        _entries = entries;
        _positions = positions;
        _otp = otp;
        _currentUser = currentUser;
        _uow = uow;
    }

    public async Task<Result> Handle(ConfirmKycWalletChangeCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure(Error.Unauthorized("Kyc.Unauthorized", "Authentication required."));

        if (!Domain.Compliance.WalletAddress.IsValid(request.WalletAddress))
            return Result.Failure(Error.Validation("Kyc.InvalidWallet", "Invalid EVM wallet address."));

        var profile = await _kyc.GetByUserIdAsync(userId, ct);
        if (profile is null)
            return Result.Failure(Error.NotFound("Kyc.NotFound", "No KYC profile exists for this user."));
        if (string.IsNullOrWhiteSpace(profile.WalletAddress))
            return Result.Failure(Error.Conflict(
                "Kyc.NoWalletLinked", "No wallet is linked yet — link one instead of changing it."));

        var user = await _users.GetByIdAsync(userId, ct);
        if (user?.PhoneNumber is not { } phone || string.IsNullOrWhiteSpace(phone))
            return Result.Failure(Error.Conflict(
                "Kyc.NoPhone", "This account has no phone number to verify against."));

        // Second factor first: everything below reads the investor's holdings, and none of it should
        // run for a caller who has not just proved they hold the phone.
        var verification = await _otp.VerifyAsync(phone, request.Code, ct);
        if (verification.IsFailure)
            return verification;

        var current = profile.WalletAddress;
        var owned = await _entries.ListByInvestorAsync(userId, ct);

        // A batch in flight is not negotiable: the exchange is holding a row that names this address.
        // Matched against the address being replaced, not merely the investor — a request carrying an
        // older wallet, or none at all, pins nothing.
        var batched = owned.Any(e =>
            e.Status == WhitelistStatus.Batched
            && string.Equals(e.WalletAddress, current, StringComparison.OrdinalIgnoreCase));

        if (batched)
            return Result.Failure(Error.Conflict(
                "Kyc.WalletInMintBatch",
                "A mint batch naming the current wallet is with the exchange. The address can move "
                + "once that batch is executed or called off."));

        // Shares already on the address stay there. The holder may still move on — with their eyes
        // open, having been shown the number.
        var stranded = (await _positions.GetByAddressAsync(current, ct)).Sum(p => p.TokenCount);
        if (stranded > 0 && !request.AcknowledgeStrandedShares)
            return Result.Failure(Error.Conflict(
                "Kyc.WalletHasShares",
                $"{stranded} share(s) are held on the current wallet and will stay there — the "
                + "platform cannot move them. Confirm that you understand before changing the address."));

        try
        {
            profile.ReplaceWallet(request.WalletAddress);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Validation("Kyc.WalletNotChanged", ex.Message));
        }

        _kyc.Update(profile);

        // Requests still in the queue follow the holder; SetWallet refuses the ones that must not
        // move, so a batched row keeps the address the exchange was given.
        foreach (var entry in owned.Where(e => e.Status is WhitelistStatus.Pending or WhitelistStatus.Ready))
        {
            entry.SetWallet(request.WalletAddress);
            _entries.Update(entry);
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
