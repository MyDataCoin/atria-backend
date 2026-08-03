using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Governance.Executors;
using Atria.Application.Payouts.Commands;
using Atria.Application.Payouts.Queries;
using Atria.Domain.Governance;
using Atria.Domain.Holders;
using Atria.Domain.Investments;
using Atria.Domain.Payouts;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Payouts;

/// <summary>
/// The distribution flow end to end at the application layer: drawing one up against a frozen
/// register, opening it only on a second approval, and recording what happened to each holder's
/// money.
/// </summary>
public sealed class PayoutFlowTests
{
    private readonly IPayoutRunRepository _runs = Substitute.For<IPayoutRunRepository>();
    private readonly IHolderSnapshotRepository _snapshots = Substitute.For<IHolderSnapshotRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly DateTime Cut = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _operator = Guid.NewGuid();
    private readonly Guid _investorA = Guid.NewGuid();
    private readonly Guid _investorB = Guid.NewGuid();

    public PayoutFlowTests()
    {
        _currentUser.UserId.Returns(_operator);
        _clock.UtcNow.Returns(Now);
    }

    private HolderSnapshot GivenRegister(SnapshotPurpose purpose = SnapshotPurpose.Payout)
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");
        var snapshot = HolderSnapshot.Create(
            property.Id, Cut, purpose, 1_000_000, _operator,
            [
                new HolderSnapshotEntry("0xaaa", 700, _investorA),
                new HolderSnapshotEntry("0xbbb", 300, _investorB)
            ]);

        _snapshots.GetWithRowsAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(snapshot);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return snapshot;
    }

    private CreatePayoutRunCommandHandler NewCreateHandler()
        => new(_runs, _snapshots, _properties, _currentUser, _audit, _uow);

    private PayoutSettlementHandlers NewSettlementHandlers()
        => new(_runs, _clock, _audit, _uow);

    private PayoutRun Captured()
        => (PayoutRun)_runs.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(IPayoutRunRepository.AddAsync))
            .GetArguments()[0]!;

    private void GivenStoredRun(PayoutRun run)
        => _runs.GetWithItemsAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);

    [Fact]
    public async Task A_distribution_is_computed_against_the_frozen_register()
    {
        var snapshot = GivenRegister();

        var result = await NewCreateHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.Dividend, PayoutMethod.BankTransfer, 10_000m, "KGS",
                "Дивиденды за II квартал"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var run = Captured();
        run.SnapshotId.Should().Be(snapshot.Id);
        run.Items.Should().HaveCount(2);
        run.Items.Single(i => i.InvestorId == _investorA).Amount.Should().Be(7_000m);
        run.Items.Single(i => i.InvestorId == _investorB).Amount.Should().Be(3_000m);
    }

    /// <summary>Nothing is authorised by drawing one up — that is the whole point of the draft.</summary>
    [Fact]
    public async Task A_new_distribution_authorises_nothing()
    {
        var snapshot = GivenRegister();

        await NewCreateHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.Dividend, PayoutMethod.Wallet, 10_000m, "KGS", null),
            CancellationToken.None);

        Captured().Status.Should().Be(PayoutRunStatus.Draft);
    }

    /// <summary>
    /// A second run on the same cut would divide the same register twice and pay every holder
    /// double — the kind of mistake found only after the transfers have gone.
    /// </summary>
    [Fact]
    public async Task The_same_cut_cannot_be_distributed_twice()
    {
        var snapshot = GivenRegister();
        _runs.FindBySnapshotAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(PayoutRun.Create(
                snapshot, PayoutKind.Dividend, PayoutMethod.Wallet, 10_000m, "KGS", _operator, null));

        var result = await NewCreateHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.Dividend, PayoutMethod.Wallet, 10_000m, "KGS", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await _runs.DidNotReceive().AddAsync(Arg.Any<PayoutRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_reporting_cut_is_refused_as_a_basis_for_paying_money()
    {
        var snapshot = GivenRegister(SnapshotPurpose.Reporting);

        var result = await NewCreateHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.Dividend, PayoutMethod.Wallet, 10_000m, "KGS", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    /// <summary>The second signature is what turns arithmetic on a page into authority to pay.</summary>
    [Fact]
    public async Task The_second_approval_is_what_opens_a_distribution_for_settlement()
    {
        var run = PayoutRun.Create(
            GivenRegister(), PayoutKind.Dividend, PayoutMethod.BankTransfer, 10_000m, "KGS",
            _operator, null);
        GivenStoredRun(run);

        var action = CriticalAction.Request(
            CriticalActionKind.PayoutRun, run.Id, "Дивиденды за II квартал", _operator, Now);

        var result = await new ApprovePayoutRunExecutor(_runs, _clock, _audit)
            .ExecuteAsync(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        run.Status.Should().Be(PayoutRunStatus.Approved);
        run.ApprovedAtUtc.Should().Be(Now);
    }

    /// <summary>
    /// A run cancelled while the approval sat waiting must not come back to life. Failing here
    /// leaves the request pending rather than recording an approval of something that did not happen.
    /// </summary>
    [Fact]
    public async Task Approving_a_distribution_that_was_called_off_is_a_conflict()
    {
        var run = PayoutRun.Create(
            GivenRegister(), PayoutKind.Dividend, PayoutMethod.Wallet, 10_000m, "KGS", _operator, null);
        run.Cancel("Ошибка в сумме");
        GivenStoredRun(run);

        var action = CriticalAction.Request(CriticalActionKind.PayoutRun, run.Id, null, _operator, Now);

        var result = await new ApprovePayoutRunExecutor(_runs, _clock, _audit)
            .ExecuteAsync(action, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Settling_a_line_records_the_reference_and_journals_it()
    {
        var run = ApprovedRun();
        var item = run.Items.First();

        var result = await NewSettlementHandlers().Handle(
            new SettlePayoutItemCommand(run.Id, item.Id, "ПП-2026-0042"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        item.Status.Should().Be(PayoutItemStatus.Paid);
        item.SettlementReference.Should().Be("ПП-2026-0042");
        await _audit.Received().WriteAsync(
            Arg.Any<string>(), run.Id, "PayoutItemSettled", Arg.Any<string>(),
            Arg.Any<Domain.Audit.AuditSeverity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Settling_before_the_second_approval_is_refused()
    {
        var run = PayoutRun.Create(
            GivenRegister(), PayoutKind.Dividend, PayoutMethod.Wallet, 10_000m, "KGS", _operator, null);
        GivenStoredRun(run);

        var result = await NewSettlementHandlers().Handle(
            new SettlePayoutItemCommand(run.Id, run.Items.First().Id, "ПП-1"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task A_failed_payment_stays_owed_and_is_journalled_loudly()
    {
        var run = ApprovedRun();
        var item = run.Items.First();

        var result = await NewSettlementHandlers().Handle(
            new FailPayoutItemCommand(run.Id, item.Id, "Счёт закрыт"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        run.OutstandingAmount.Should().Be(10_000m);
        await _audit.Received().WriteAsync(
            Arg.Any<string>(), run.Id, "PayoutItemFailed", Arg.Any<string>(),
            Domain.Audit.AuditSeverity.Alert, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_distribution_cannot_be_closed_over_a_holder_nobody_looked_at()
    {
        var run = ApprovedRun();
        await NewSettlementHandlers().Handle(
            new SettlePayoutItemCommand(run.Id, run.Items.First().Id, "ПП-1"), CancellationToken.None);

        var result = await NewSettlementHandlers().Handle(
            new CompletePayoutRunCommand(run.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        run.Status.Should().Be(PayoutRunStatus.Approved);
    }

    /// <summary>
    /// A holder is never told they are owed money that nobody has authorised, and never shown a
    /// distribution that was called off.
    /// </summary>
    [Fact]
    public async Task An_investor_sees_only_distributions_that_were_actually_authorised()
    {
        var snapshot = GivenRegister();
        var draft = PayoutRun.Create(
            snapshot, PayoutKind.Dividend, PayoutMethod.Wallet, 10_000m, "KGS", _operator, null);
        var approved = PayoutRun.Create(
            snapshot, PayoutKind.Dividend, PayoutMethod.BankTransfer, 20_000m, "KGS", _operator, null);
        approved.Approve(Now);

        _currentUser.UserId.Returns(_investorA);
        _runs.ListForInvestorAsync(_investorA, Arg.Any<CancellationToken>()).Returns(
        [
            (draft, draft.Items.Single(i => i.InvestorId == _investorA)),
            (approved, approved.Items.Single(i => i.InvestorId == _investorA))
        ]);

        var result = await new GetMyPayoutsQueryHandler(_runs, _currentUser)
            .Handle(new GetMyPayoutsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Amount.Should().Be(14_000m);
    }

    private PayoutRun ApprovedRun()
    {
        var run = PayoutRun.Create(
            GivenRegister(), PayoutKind.Dividend, PayoutMethod.BankTransfer, 10_000m, "KGS",
            _operator, null);
        run.Approve(Now);
        GivenStoredRun(run);
        return run;
    }
}
