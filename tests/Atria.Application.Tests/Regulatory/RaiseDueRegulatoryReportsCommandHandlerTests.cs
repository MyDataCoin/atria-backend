using Atria.Application.Abstractions;
using Atria.Application.Regulatory.Commands;
using Atria.Domain.Investments;
using Atria.Domain.Regulatory;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Regulatory;

/// <summary>
/// The periodic sweep must record every closed period exactly once: a missing obligation is a missed
/// deadline, and a duplicated one is a second deadline for the same filing.
/// </summary>
public sealed class RaiseDueRegulatoryReportsCommandHandlerTests
{
    private readonly IRegulatoryReportRepository _reports = Substitute.For<IRegulatoryReportRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly DateTime AsOf = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly List<RegulatoryReport> _added = new();

    private RaiseDueRegulatoryReportsCommandHandler NewHandler()
    {
        _reports.When(r => r.AddAsync(Arg.Any<RegulatoryReport>(), Arg.Any<CancellationToken>()))
            .Do(c => _added.Add(c.Arg<RegulatoryReport>()));
        return new RaiseDueRegulatoryReportsCommandHandler(_reports, _properties, _uow);
    }

    private Property GivenOpenIssue()
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");
        property.Publish();
        _properties.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Property> { property });
        return property;
    }

    [Fact]
    public async Task Closed_months_get_a_placement_and_a_collateral_obligation_per_issue()
    {
        var property = GivenOpenIssue();

        var result = await NewHandler().Handle(
            new RaiseDueRegulatoryReportsCommand(AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var july = _added.Where(r => r.PeriodEndUtc.Month == 7 && r.PeriodEndUtc.Year == 2026).ToList();
        july.Should().Contain(r => r.Kind == RegulatoryReportKind.MonthlyPlacement && r.PropertyId == property.Id);
        july.Should().Contain(r => r.Kind == RegulatoryReportKind.CollateralInterim && r.PropertyId == property.Id);
    }

    [Fact]
    public async Task The_quarterly_aml_obligation_is_platform_wide_not_per_issue()
    {
        GivenOpenIssue();

        await NewHandler().Handle(new RaiseDueRegulatoryReportsCommand(AsOf), CancellationToken.None);

        _added.Where(r => r.Kind == RegulatoryReportKind.AmlQuarterly)
            .Should().NotBeEmpty()
            .And.OnlyContain(r => r.PropertyId == null);
    }

    /// <summary>A month still running has no deadline yet — recording one would be meaningless.</summary>
    [Fact]
    public async Task The_period_still_in_progress_is_not_recorded()
    {
        GivenOpenIssue();

        await NewHandler().Handle(new RaiseDueRegulatoryReportsCommand(AsOf), CancellationToken.None);

        _added.Should().NotContain(r => r.PeriodEndUtc >= AsOf);
    }

    [Fact]
    public async Task An_obligation_already_recorded_is_left_alone()
    {
        var property = GivenOpenIssue();

        // Everything the sweep looks for already exists.
        _reports.FindAsync(
                Arg.Any<RegulatoryReportKind>(), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(RegulatoryReport.Raise(
                RegulatoryReportKind.MonthlyPlacement, property.Id,
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc)));

        var result = await NewHandler().Handle(
            new RaiseDueRegulatoryReportsCommand(AsOf), CancellationToken.None);

        result.Value.Should().Be(0);
        _added.Should().BeEmpty();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_draft_issue_carries_no_placement_obligations()
    {
        var draft = Property.Create("Not published yet", null, null, 1_000_000m, 100m, 1_000, "KGS");
        _properties.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Property> { draft });

        await NewHandler().Handle(new RaiseDueRegulatoryReportsCommand(AsOf), CancellationToken.None);

        _added.Should().NotContain(r => r.PropertyId == draft.Id);
    }

    [Fact]
    public async Task Every_recorded_obligation_carries_a_deadline_after_its_period()
    {
        GivenOpenIssue();

        await NewHandler().Handle(new RaiseDueRegulatoryReportsCommand(AsOf), CancellationToken.None);

        _added.Should().NotBeEmpty();
        _added.Should().OnlyContain(r => r.DueAtUtc > r.PeriodEndUtc);
    }
}
