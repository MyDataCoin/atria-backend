using Atria.Application.Abstractions;
using Atria.Application.Holders.EventHandlers;
using Atria.Domain.Compliance;
using Atria.Domain.Holders;
using Atria.Domain.Investments.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atria.Application.Tests.Holders;

/// <summary>
/// Verifies the our-records projection of the holder registry: an activation adds the investor's
/// tokens to their wallet position, a second distinct activation accumulates, a redelivery is a
/// no-op, and an investor with no wallet yields no position.
/// </summary>
public sealed class ProjectHolderPositionOnInvestmentActivatedHandlerTests
{
    private readonly IComplianceRepository _profiles = Substitute.For<IComplianceRepository>();
    private readonly IHolderPositionRepository _positions = Substitute.For<IHolderPositionRepository>();
    private readonly IProcessedEventStore _processed = Substitute.For<IProcessedEventStore>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private const string Wallet = "0x1111111111111111111111111111111111111111";
    private static readonly Guid InvestorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PropertyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ProjectHolderPositionOnInvestmentActivatedHandler NewHandler()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc));
        return new ProjectHolderPositionOnInvestmentActivatedHandler(
            _profiles, _positions, _processed, _clock, _uow,
            NullLogger<ProjectHolderPositionOnInvestmentActivatedHandler>.Instance);
    }

    private ComplianceProfile AllowlistedProfile(string? wallet = Wallet)
    {
        var profile = ComplianceProfile.Create(InvestorId, wallet);
        if (wallet is not null)
            profile.MarkAllowlisted();
        _profiles.GetByInvestorAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(profile);
        return profile;
    }

    private static InvestmentActivatedEvent Event(long tokenCount = 30)
        => new(Guid.NewGuid(), InvestorId, PropertyId, tokenCount, 3_000m);

    [Fact]
    public async Task Creates_a_position_for_the_investor_wallet()
    {
        AllowlistedProfile();
        _positions.GetByAddressAsync(PropertyId, Wallet, Arg.Any<CancellationToken>()).Returns((HolderPosition?)null);
        HolderPosition? captured = null;
        await _positions.AddAsync(Arg.Do<HolderPosition>(p => captured = p), Arg.Any<CancellationToken>());

        await NewHandler().HandleAsync(Event(30), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.PropertyId.Should().Be(PropertyId);
        captured.WalletAddress.Should().Be(Wallet);
        captured.TokenCount.Should().Be(30);
        captured.InvestorId.Should().Be(InvestorId);
        captured.IsAllowlisted.Should().BeTrue();
        captured.Source.Should().Be(HolderSource.OurRecords);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _processed.Received(1).MarkProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accumulates_onto_an_existing_position()
    {
        AllowlistedProfile();
        var existing = HolderPosition.Create(PropertyId, Wallet, 30, InvestorId, true, HolderSource.OurRecords,
            new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc));
        _positions.GetByAddressAsync(PropertyId, Wallet, Arg.Any<CancellationToken>()).Returns(existing);

        await NewHandler().HandleAsync(Event(20), CancellationToken.None);

        existing.TokenCount.Should().Be(50);
        await _positions.DidNotReceive().AddAsync(Arg.Any<HolderPosition>(), Arg.Any<CancellationToken>());
        _positions.Received(1).Update(existing);
    }

    [Fact]
    public async Task Redelivered_event_is_a_no_op()
    {
        _processed.IsProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        await NewHandler().HandleAsync(Event(), CancellationToken.None);

        await _profiles.DidNotReceive().GetByInvestorAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_wallet_projects_no_position_but_durably_marks_processed()
    {
        AllowlistedProfile(wallet: null);

        await NewHandler().HandleAsync(Event(), CancellationToken.None);

        await _positions.DidNotReceive().AddAsync(Arg.Any<HolderPosition>(), Arg.Any<CancellationToken>());
        // The mark must be persisted (committed) even with no position, so the event is not reprocessed.
        await _processed.Received(1).MarkProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mark_is_recorded_before_the_commit_so_effect_and_mark_are_atomic()
    {
        AllowlistedProfile();
        _positions.GetByAddressAsync(PropertyId, Wallet, Arg.Any<CancellationToken>()).Returns((HolderPosition?)null);
        var order = new List<string>();
        _processed.When(p => p.MarkProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => order.Add("mark"));
        _uow.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => order.Add("save"));

        await NewHandler().HandleAsync(Event(30), CancellationToken.None);

        order.Should().Equal("mark", "save");
    }
}
