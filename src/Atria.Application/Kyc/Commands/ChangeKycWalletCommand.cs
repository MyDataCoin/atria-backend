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
public sealed record ConfirmKycWalletChangeCommand(string WalletAddress, string Code)
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
/// The address is where shares and dividends land, so a change is refused while anything is already
/// riding on the old one: a minted position cannot follow the holder to a new address, and a request
/// sitting in a mint list names the old address on a document the exchange is already holding. Those
/// are unwound or executed first, by a person who can see why.
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
    private readonly IOtpService _otp;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _uow;

    public ConfirmKycWalletChangeCommandHandler(
        IKycRepository kyc,
        IUserRepository users,
        IWhitelistEntryRepository entries,
        IOtpService otp,
        ICurrentUserService currentUser,
        IUnitOfWork uow)
    {
        _kyc = kyc;
        _users = users;
        _entries = entries;
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

        // What pins the address is a request that NAMES it and has left the queue: minted shares sit
        // at that address, and a batched row is on a document the exchange already holds. A request
        // carrying some other address — an earlier wallet, or none at all — pins nothing, so the
        // comparison is against the address being replaced, not merely against the investor.
        var current = profile.WalletAddress;
        var owned = await _entries.ListByInvestorAsync(userId, ct);
        var pinned = owned.Any(e =>
            e.Status is WhitelistStatus.Minted or WhitelistStatus.Batched
            && string.Equals(e.WalletAddress, current, StringComparison.OrdinalIgnoreCase));

        if (pinned)
            return Result.Failure(Error.Conflict(
                "Kyc.WalletHasShares",
                "Shares have already been issued to the current wallet, or a mint batch is in flight. "
                + "Contact support to move the address."));

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
