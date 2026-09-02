using Atria.Application.Abstractions;
using Atria.Application.Payouts.Commands;
using Atria.Domain.Holders;
using Atria.Domain.Investments;
using Atria.Domain.Operations;
using Atria.Domain.Payouts;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Payouts;

/// <summary>
/// A dividend has to say where its money came from. Before this it was a number typed into a form:
/// divided across the register perfectly, and traceable to nothing.
/// <para>
/// The rules pinned here are what make it traceable — a dividend names a confirmed period, cannot
/// exceed what that period earned, and closes it so the same income is not distributed twice. A
/// capital return is deliberately outside all of this: it is the issue being wound up, not income
/// being shared.
/// </para>
/// </summary>
public sealed class PayoutFromOperatingPeriodTests
{
    private readonly IPayoutRunRepository _runs = Substitute.For<IPayoutRunRepository>();
    private readonly IHolderSnapshotRepository _snapshots = Substitute.For<IHolderSnapshotRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IOperatingPeriodRepository _periods = Substitute.For<IOperatingPeriodRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly DateTime Cut = new(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2028, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _operator = Guid.NewGuid();
    private readonly Guid _accountant = Guid.NewGuid();
    private Property _property = null!;

    public PayoutFromOperatingPeriodTests() => _currentUser.UserId.Returns(_operator);

    private CreatePayoutRunCommandHandler NewHandler()
        => new(_runs, _snapshots, _properties, _periods, _currentUser, _audit, _uow);

    private HolderSnapshot GivenRegister()
    {
        _property = Property.Create("ЖК на Токомбаева", null, null, 1_000_000m, 100m, 1_000, "KGS");
        var snapshot = HolderSnapshot.Create(
            _property.Id, Cut, SnapshotPurpose.Payout, 1_000, _operator,
            [
                new HolderSnapshotEntry("0xaaa", 700, Guid.NewGuid()),
                new HolderSnapshotEntry("0xbbb", 300, Guid.NewGuid())
            ]);

        _snapshots.GetWithRowsAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(snapshot);
        _properties.GetByIdAsync(_property.Id, Arg.Any<CancellationToken>()).Returns(_property);
        return snapshot;
    }

    // 500 000 in, 120 000 out — 380 000 available to distribute.
    private OperatingPeriod GivenConfirmedPeriod(Guid propertyId)
    {
        var period = OperatingPeriod.Report(
            propertyId, Cut.AddMonths(-3), Cut, 500_000m, 120_000m, "KGS", _accountant, null);
        period.Confirm(_operator, Now);
        _periods.GetByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(period);
        return period;
    }

    [Fact]
    public async Task A_dividend_is_declared_from_a_confirmed_period_and_closes_it()
    {
        var snapshot = GivenRegister();
        var period = GivenConfirmedPeriod(snapshot.PropertyId);

        var result = await NewHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.Dividend, PayoutMethod.BankTransfer, 380_000m, "KGS",
                "IV квартал", period.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        period.Status.Should().Be(OperatingPeriodStatus.Distributed);
        period.PayoutRunId.Should().Be(result.Value);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dividend_without_a_period_is_refused()
    {
        var snapshot = GivenRegister();

        var result = await NewHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.Dividend, PayoutMethod.BankTransfer, 10_000m, "KGS", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("payout.periodRequired");
        await _runs.DidNotReceive().AddAsync(Arg.Any<PayoutRun>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dividend_cannot_exceed_what_the_period_earned()
    {
        var snapshot = GivenRegister();
        var period = GivenConfirmedPeriod(snapshot.PropertyId);

        var result = await NewHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.Dividend, PayoutMethod.BankTransfer, 380_000.01m, "KGS",
                null, period.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        period.Status.Should().Be(OperatingPeriodStatus.Confirmed);
    }

    [Fact]
    public async Task A_dividend_cannot_be_declared_from_an_unconfirmed_period()
    {
        var snapshot = GivenRegister();
        var draft = OperatingPeriod.Report(
            snapshot.PropertyId, Cut.AddMonths(-3), Cut, 500_000m, 120_000m, "KGS", _accountant, null);
        _periods.GetByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);

        var result = await NewHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.Dividend, PayoutMethod.BankTransfer, 100_000m, "KGS",
                null, draft.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("payout.periodUnavailable");
    }

    [Fact]
    public async Task A_period_belonging_to_another_issue_is_refused()
    {
        // Otherwise one issue's income could fund another's dividend.
        var snapshot = GivenRegister();
        var foreign = GivenConfirmedPeriod(Guid.NewGuid());

        var result = await NewHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.Dividend, PayoutMethod.BankTransfer, 100_000m, "KGS",
                null, foreign.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("payout.periodMismatch");
    }

    [Fact]
    public async Task A_capital_return_needs_no_period_and_refuses_one()
    {
        var snapshot = GivenRegister();
        var period = GivenConfirmedPeriod(snapshot.PropertyId);

        var withoutPeriod = await NewHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.CapitalReturn, PayoutMethod.BankTransfer, 10_000m, "KGS", null),
            CancellationToken.None);
        withoutPeriod.IsSuccess.Should().BeTrue();

        var withPeriod = await NewHandler().Handle(
            new CreatePayoutRunCommand(
                snapshot.Id, PayoutKind.CapitalReturn, PayoutMethod.BankTransfer, 10_000m, "KGS",
                null, period.Id),
            CancellationToken.None);

        withPeriod.IsSuccess.Should().BeFalse();
        withPeriod.Error.Code.Should().Be("payout.periodNotApplicable");
        period.Status.Should().Be(OperatingPeriodStatus.Confirmed);
    }
}
