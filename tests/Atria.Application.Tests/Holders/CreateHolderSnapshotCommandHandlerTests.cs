using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Holders.Commands;
using Atria.Domain.Holders;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Holders;

/// <summary>
/// Verifies what makes a snapshot usable as the basis for a payout or a regulatory statement: it is
/// built from the holdings at the cut, taking it twice for the same cut yields the same snapshot, and
/// holdings that change afterwards leave it untouched.
/// </summary>
public sealed class CreateHolderSnapshotCommandHandlerTests
{
    private readonly IHolderSnapshotRepository _snapshots = Substitute.For<IHolderSnapshotRepository>();
    private readonly IHolderPositionRepository _positions = Substitute.For<IHolderPositionRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly DateTime Now = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid InvestorA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid InvestorB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string WalletA = "0xaaaa000000000000000000000000000000000000";
    private const string WalletB = "0xbbbb000000000000000000000000000000000000";

    private readonly Property _property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "USD");

    public CreateHolderSnapshotCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _currentUser.UserId.Returns(OperatorId);
        _properties.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_property);
    }

    private CreateHolderSnapshotCommandHandler NewHandler() =>
        new(_snapshots, _positions, _properties, _currentUser, _clock, _uow);

    private void GivenPositions(params (string Wallet, long Tokens, Guid? Investor)[] holdings)
        => _positions.GetByPropertyAsync(_property.Id, Arg.Any<CancellationToken>())
            .Returns(holdings
                .Select(h => HolderPosition.Create(
                    _property.Id, h.Wallet, h.Tokens, h.Investor, true, HolderSource.OurRecords, Now))
                .ToList());

    /// <summary>Captures the snapshot the handler hands to the repository.</summary>
    private HolderSnapshot? Added()
        => _snapshots.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IHolderSnapshotRepository.AddAsync))
            .Select(c => (HolderSnapshot)c.GetArguments()[0]!)
            .LastOrDefault();

    [Fact]
    public async Task Snapshot_is_built_from_the_holdings_at_the_cut()
    {
        GivenPositions((WalletB, 300, InvestorB), (WalletA, 700, InvestorA));

        var result = await NewHandler().Handle(
            new CreateHolderSnapshotCommand(_property.Id, null, SnapshotPurpose.Payout), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var snapshot = Added()!;
        snapshot.TotalTokens.Should().Be(1_000);
        snapshot.AddressCount.Should().Be(2);
        snapshot.SnapshotAtUtc.Should().Be(Now);
        snapshot.CreatedByUserId.Should().Be(OperatorId);
        snapshot.Rows.Single(r => r.WalletAddress == WalletA).Share.Should().Be(0.7m);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Taking_the_same_cut_twice_returns_the_snapshot_already_taken()
    {
        GivenPositions((WalletA, 700, InvestorA));
        var cut = Now.AddHours(-1);

        var existing = HolderSnapshot.Create(
            _property.Id, cut, SnapshotPurpose.Payout, null, OperatorId,
            new[] { new HolderSnapshotEntry(WalletA, 700, InvestorA) });
        _snapshots.FindByCutAsync(_property.Id, cut, SnapshotPurpose.Payout, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await NewHandler().Handle(
            new CreateHolderSnapshotCommand(_property.Id, cut, SnapshotPurpose.Payout), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existing.Id);
        await _snapshots.DidNotReceive().AddAsync(Arg.Any<HolderSnapshot>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The point of a snapshot: a trade settling after the cut must not move the register the payout
    /// was computed against.
    /// </summary>
    [Fact]
    public async Task Holdings_that_change_after_the_cut_do_not_alter_the_snapshot()
    {
        GivenPositions((WalletA, 700, InvestorA), (WalletB, 300, InvestorB));

        await NewHandler().Handle(
            new CreateHolderSnapshotCommand(_property.Id, null, SnapshotPurpose.Payout), CancellationToken.None);
        var snapshot = Added()!;

        // WalletA sells its entire holding to WalletB after the cut.
        GivenPositions((WalletA, 0, InvestorA), (WalletB, 1_000, InvestorB));

        snapshot.TotalTokens.Should().Be(1_000);
        snapshot.Rows.Single(r => r.WalletAddress == WalletA).TokenCount.Should().Be(700);
        snapshot.Rows.Single(r => r.WalletAddress == WalletB).TokenCount.Should().Be(300);
    }

    [Fact]
    public async Task Two_purposes_on_the_same_cut_are_separate_snapshots()
    {
        GivenPositions((WalletA, 700, InvestorA));
        var cut = Now.AddHours(-2);

        var payout = HolderSnapshot.Create(
            _property.Id, cut, SnapshotPurpose.Payout, null, OperatorId,
            new[] { new HolderSnapshotEntry(WalletA, 700, InvestorA) });
        _snapshots.FindByCutAsync(_property.Id, cut, SnapshotPurpose.Payout, Arg.Any<CancellationToken>())
            .Returns(payout);

        var result = await NewHandler().Handle(
            new CreateHolderSnapshotCommand(_property.Id, cut, SnapshotPurpose.Reporting), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(payout.Id);
        Added()!.Purpose.Should().Be(SnapshotPurpose.Reporting);
    }

    [Fact]
    public async Task Zero_balance_addresses_are_left_out_of_the_snapshot()
    {
        GivenPositions((WalletA, 700, InvestorA), (WalletB, 0, InvestorB));

        await NewHandler().Handle(
            new CreateHolderSnapshotCommand(_property.Id, null, SnapshotPurpose.Reporting), CancellationToken.None);

        var snapshot = Added()!;
        snapshot.AddressCount.Should().Be(1);
        snapshot.Rows.Should().ContainSingle(r => r.WalletAddress == WalletA);
    }

    [Fact]
    public async Task A_cut_in_the_future_is_rejected()
    {
        GivenPositions((WalletA, 700, InvestorA));

        var result = await NewHandler().Handle(
            new CreateHolderSnapshotCommand(_property.Id, Now.AddMinutes(1), SnapshotPurpose.Payout),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Snapshotting_an_unknown_property_is_a_not_found()
    {
        _properties.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Property?)null);

        var result = await NewHandler().Handle(
            new CreateHolderSnapshotCommand(Guid.NewGuid(), null, SnapshotPurpose.Payout), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_take_a_snapshot()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await NewHandler().Handle(
            new CreateHolderSnapshotCommand(_property.Id, null, SnapshotPurpose.Payout), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }
}
