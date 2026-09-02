using Atria.Application.Abstractions;
using Atria.Application.Investments.Services;
using Atria.Application.Properties.Commands;
using Atria.Domain.Compliance;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Refunds;
using Atria.Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Properties;

/// <summary>
/// The management company chose "возврат и продление" for a placement that misses its target. This
/// covers the return half: every application is voided, every share goes back on sale, and everyone
/// who had actually been placed is owed their money.
/// <para>
/// The distinction that carries the whole thing is reserved vs active. A reserved application holds
/// shares but never received any and was never charged, so it releases its shares and owes nothing;
/// an active one was placed and must be refunded. Refunding a reservation would hand back money that
/// never came in.
/// </para>
/// </summary>
public sealed class ClosePlacementUnsubscribedCommandHandlerTests
{
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly IHolderPositionRepository _positions = Substitute.For<IHolderPositionRepository>();
    private readonly IRefundObligationRepository _refunds = Substitute.For<IRefundObligationRepository>();
    private readonly IComplianceRepository _profiles = Substitute.For<IComplianceRepository>();
    private readonly IWhitelistEntryRepository _whitelist = Substitute.For<IWhitelistEntryRepository>();
    private readonly IBlockchainOperationQueue _chain = Substitute.For<IBlockchainOperationQueue>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid InvestorA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid InvestorB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly List<RefundObligation> _raised = new();

    private ClosePlacementUnsubscribedCommandHandler NewHandler()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(Role.Admin).Returns(true);
        _clock.UtcNow.Returns(new DateTime(2027, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        _refunds.When(r => r.AddAsync(Arg.Any<RefundObligation>(), Arg.Any<CancellationToken>()))
            .Do(c => _raised.Add(c.Arg<RefundObligation>()));

        var unwinder = new InvestmentUnwinder(_positions, _refunds, _profiles, _whitelist, _chain, _clock);
        return new ClosePlacementUnsubscribedCommandHandler(
            _properties, _investments, unwinder, _currentUser, _audit, _uow);
    }

    // 1 000 shares at 1 000 KGS, target 750 000 — the placement below raises 400 000 and misses it.
    private Property GivenOpenIssue()
    {
        var property = Property.Create("ЖК на Токомбаева", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");
        property.Publish();
        property.SchedulePlacement(
            new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            750_000m);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    private Investment GivenApplication(Property property, Guid investor, long tokens, bool active)
    {
        var investment = InvestmentFactory.CreateForInvestor(
            investor, property.Id, tokens, property.Currency, property.TokenPrice,
            DateTime.UtcNow.AddDays(3));

        property.ReserveTokens(tokens);
        if (active)
            investment.Approve(DateTime.UtcNow);

        return investment;
    }

    private void GivenApplications(Guid propertyId, params Investment[] investments)
        => _investments.ListAsync(null, propertyId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(investments.ToList());

    [Fact]
    public async Task Unwinding_refunds_the_placed_and_returns_every_share()
    {
        var property = GivenOpenIssue();
        var placed = GivenApplication(property, InvestorA, 300L, active: true);
        var reserved = GivenApplication(property, InvestorB, 100L, active: false);
        GivenApplications(property.Id, placed, reserved);

        property.AvailableTokens.Should().Be(600L);
        property.IsTargetMet.Should().BeFalse();

        var result = await NewHandler().Handle(
            new ClosePlacementUnsubscribedCommand(property.Id, "не собрана целевая сумма"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Every share is back on sale, whether it had been placed or merely held.
        property.AvailableTokens.Should().Be(1_000L);
        placed.Status.Should().Be(InvestmentStatus.Annulled);
        reserved.Status.Should().Be(InvestmentStatus.Annulled);

        // Only the placed application is owed money — the reservation was never charged.
        _raised.Should().ContainSingle();
        var refund = _raised.Single();
        refund.InvestorId.Should().Be(InvestorA);
        refund.Amount.Should().Be(300_000m);
        refund.TokenCount.Should().Be(300L);
        refund.Reason.Should().Be(RefundReason.PlacementNotSubscribed);
        refund.Status.Should().Be(RefundStatus.Pending);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unwinding_stops_sales_and_completes_the_issue_rather_than_invalidating_it()
    {
        // Nothing here was unlawful — the offering did not fill. Invalidated means something else (§73).
        var property = GivenOpenIssue();
        GivenApplications(property.Id);

        await NewHandler().Handle(
            new ClosePlacementUnsubscribedCommand(property.Id, "не собрана целевая сумма"),
            CancellationToken.None);

        property.Status.Should().Be(PropertyStatus.Completed);
        property.SalesPaused.Should().BeTrue();
    }

    [Fact]
    public async Task Unwinding_leaves_already_closed_applications_alone()
    {
        var property = GivenOpenIssue();
        var live = GivenApplication(property, InvestorA, 200L, active: true);
        var cancelled = GivenApplication(property, InvestorB, 100L, active: false);
        cancelled.Cancel();
        property.ReleaseTokens(100L);

        // The handler filters on status, so a cancelled application must not be annulled a second
        // time nor have its already-returned shares released again.
        GivenApplications(property.Id, live, cancelled);

        var result = await NewHandler().Handle(
            new ClosePlacementUnsubscribedCommand(property.Id, "не собрана целевая сумма"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        cancelled.Status.Should().Be(InvestmentStatus.Cancelled);
        property.AvailableTokens.Should().Be(1_000L);
        _raised.Should().ContainSingle();
    }

    [Fact]
    public async Task A_reason_is_required()
    {
        var property = GivenOpenIssue();
        GivenApplications(property.Id);

        var result = await NewHandler().Handle(
            new ClosePlacementUnsubscribedCommand(property.Id, "   "), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_an_administrator_can_unwind_a_placement()
    {
        var property = GivenOpenIssue();
        GivenApplications(property.Id);
        _currentUser.IsAuthenticated.Returns(true);

        var handler = NewHandler();
        _currentUser.IsInRole(Role.Admin).Returns(false);

        var result = await handler.Handle(
            new ClosePlacementUnsubscribedCommand(property.Id, "не собрана целевая сумма"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        property.Status.Should().Be(PropertyStatus.Open);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
