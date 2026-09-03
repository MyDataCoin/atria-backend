using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Kyc.Commands;
using Atria.Domain.Kyc;
using Atria.Domain.Whitelist;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Kyc;

/// <summary>
/// Moving the allocation address, confirmed by SMS.
/// <para>
/// What may block the move is narrow on purpose: a request that NAMES the current address and has
/// left the queue. Shares minted to it sit at that address and cannot follow the holder; a batched
/// row is on a document the exchange already holds. Anything else — a request carrying an older
/// address, one with no address yet, or another investor's batch entirely — pins nothing, and
/// refusing on it would strand a holder who has issued nothing.
/// </para>
/// </summary>
public sealed class ConfirmKycWalletChangeCommandHandlerTests
{
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IWhitelistEntryRepository _entries = Substitute.For<IWhitelistEntryRepository>();
    private readonly IHolderPositionRepository _positions = Substitute.For<IHolderPositionRepository>();
    private readonly IOtpService _otp = Substitute.For<IOtpService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Current = "0x7F2C9A3F3B4E1D8B8D2A4F6E6B1c2D3e4F5a6B7c";
    private const string Next = "0x329c02528676DDef013B27AaD275d4c406927029";
    private const string Other = "0x23E3C895cC4f77B85443feE0042Dc105BeB00B93";

    private static readonly DateTime Now = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Shares sitting on an address in the register — what a change would strand.</summary>
    private void GivenHeldShares(string wallet, long tokens)
        => _positions.GetByAddressAsync(wallet, Arg.Any<CancellationToken>())
            .Returns(new List<Domain.Holders.HolderPosition>
            {
                Domain.Holders.HolderPosition.Create(
                    Guid.NewGuid(), wallet, tokens, UserId, true,
                    Domain.Holders.HolderSource.Chain, Now),
            });

    private ConfirmKycWalletChangeCommandHandler NewHandler(params WhitelistEntry[] entries)
    {
        _currentUser.UserId.Returns(UserId);
        _otp.VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var profile = KycProfile.Create(UserId);
        profile.LinkWallet(Current);
        _kyc.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(profile);

        var user = Domain.Users.User.CreateFromPhone("+996700000000", Domain.Users.Role.Investor);
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);

        _entries.ListByInvestorAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(entries.ToList());

        // Nothing held anywhere unless a test says otherwise.
        _positions.GetByAddressAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Domain.Holders.HolderPosition>());

        return new ConfirmKycWalletChangeCommandHandler(
            _kyc, _users, _entries, _positions, _otp, _currentUser, _uow);
    }

    private static WhitelistEntry Entry(string? wallet) => WhitelistEntry.Queue(
        Guid.NewGuid(), UserId, Guid.NewGuid(), 8, wallet, Now);

    private static WhitelistEntry Minted(string wallet)
    {
        var e = Entry(wallet);
        e.MarkReady(Now);
        e.IncludeIn(Guid.NewGuid());
        e.MarkMinted(Now);
        return e;
    }

    private static WhitelistEntry Batched(string wallet)
    {
        var e = Entry(wallet);
        e.MarkReady(Now);
        e.IncludeIn(Guid.NewGuid());
        return e;
    }

    [Fact]
    public async Task MovesTheAddressWhenNothingHasBeenIssued()
    {
        var handler = NewHandler();

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "111111"), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RefusesHeldSharesUntilTheHolderSaysTheyUnderstand()
    {
        var handler = NewHandler(Minted(Current));
        GivenHeldShares(Current, 8);

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "111111"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Kyc.WalletHasShares");
        // The number is in the message: the holder decides knowing what stays behind.
        result.Error.Message.Should().Contain("8");
    }

    [Fact]
    public async Task AllowsTheMoveOnceTheHolderAcknowledgesTheStrandedShares()
    {
        var handler = NewHandler(Minted(Current));
        GivenHeldShares(Current, 8);

        var result = await handler.Handle(
            new ConfirmKycWalletChangeCommand(Next, "111111", AcknowledgeStrandedShares: true), default);

        // Refusing outright would trap a holder who has lost the old key and must move on.
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RefusesABatchInFlightEvenWhenAcknowledged()
    {
        var handler = NewHandler(Batched(Current));

        var result = await handler.Handle(
            new ConfirmKycWalletChangeCommand(Next, "111111", AcknowledgeStrandedShares: true), default);

        // Not the holder's to accept: the exchange is acting on a row that names this address.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Kyc.WalletInMintBatch");
    }

    [Fact]
    public async Task AllowsTheMoveWhenTheHeldSharesSitAtAnOlderAddress()
    {
        // The holder already moved once; those shares stay where they went, and this address is free.
        var handler = NewHandler(Minted(Other));
        GivenHeldShares(Other, 8);

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "111111"), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task NeedsNoAcknowledgementWhenTheAddressHoldsNothing()
    {
        // A minted request whose shares have since moved off the address strands nothing.
        var handler = NewHandler(Minted(Current));

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "111111"), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AllowsTheMoveWhenQueuedRequestsCarryNoAddressYet()
    {
        // Approved but never batched, and with nothing to mint to — it pins nothing.
        var handler = NewHandler(Entry(null));

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "111111"), default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task QueuedRequestsFollowTheHolderToTheNewAddress()
    {
        var queued = Entry(Current);
        var handler = NewHandler(queued);

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "111111"), default);

        result.IsSuccess.Should().BeTrue();
        // A typo corrected before anything is batched should not cost the investor a reapplication.
        queued.WalletAddress.Should().Be(Next);
    }

    [Fact]
    public async Task RefusesAWrongCodeBeforeReadingAnyHoldings()
    {
        var handler = NewHandler(Minted(Current));
        GivenHeldShares(Current, 8);
        _otp.VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Validation("otp.invalid", "bad code")));

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "000000"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("otp.invalid");
    }
}
