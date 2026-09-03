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
/// One thing blocks the move: a batch NAMING the current address that is already with the exchange,
/// which is about to mint against a document nobody here can rewrite. Minted shares do not block it —
/// they stay on the address they went to, and wanting a different address for what comes next is an
/// ordinary thing to want. A batch carrying an older address pins nothing.
/// </para>
/// </summary>
public sealed class ConfirmKycWalletChangeCommandHandlerTests
{
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IWhitelistEntryRepository _entries = Substitute.For<IWhitelistEntryRepository>();
    private readonly IOtpService _otp = Substitute.For<IOtpService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Current = "0x7F2C9A3F3B4E1D8B8D2A4F6E6B1c2D3e4F5a6B7c";
    private const string Next = "0x329c02528676DDef013B27AaD275d4c406927029";
    private const string Other = "0x23E3C895cC4f77B85443feE0042Dc105BeB00B93";

    private static readonly DateTime Now = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

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

        return new ConfirmKycWalletChangeCommandHandler(
            _kyc, _users, _entries, _otp, _currentUser, _uow);
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
    public async Task AllowsTheMoveWhenSharesWereAlreadyMintedToTheCurrentAddress()
    {
        var handler = NewHandler(Minted(Current));

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "111111"), default);

        // Minted shares stay on the address they were issued to; wanting a different address for
        // what comes next is ordinary, not something to prevent.
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RefusesWhileABatchNamingTheCurrentAddressIsWithTheExchange()
    {
        var handler = NewHandler(Batched(Current));

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "111111"), default);

        // The exchange is about to mint against a document that names this address.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Kyc.WalletInMintBatch");
    }

    [Fact]
    public async Task AllowsTheMoveWhenTheBatchInFlightNamesAnOlderAddress()
    {
        // The holder moved once already; that batch pins the address it names, not this one.
        var handler = NewHandler(Batched(Other));

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
        var handler = NewHandler(Batched(Current));
        _otp.VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Validation("otp.invalid", "bad code")));

        var result = await handler.Handle(new ConfirmKycWalletChangeCommand(Next, "000000"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("otp.invalid");
    }
}
