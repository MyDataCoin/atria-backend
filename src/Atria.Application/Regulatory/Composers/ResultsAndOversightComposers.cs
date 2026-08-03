using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Holders;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;
using Atria.Domain.Regulatory;
using Atria.Domain.Users;

namespace Atria.Application.Regulatory.Composers;

/// <summary>
/// §§50, 52 (annex 6): results of the issue and of the public placement.
///
/// The holder figures are taken from a frozen snapshot, never from live positions: results filed on
/// one day and recomputed a week later must not disagree because someone traded in between. A
/// snapshot cut for reporting at or before the period end is required — without one there is nothing
/// defensible to file.
/// </summary>
public sealed class PlacementResultsComposer : IRegulatoryReportComposer
{
    private readonly IPropertyRepository _properties;
    private readonly IHolderSnapshotRepository _snapshots;

    public PlacementResultsComposer(IPropertyRepository properties, IHolderSnapshotRepository snapshots)
    {
        _properties = properties;
        _snapshots = snapshots;
    }

    public RegulatoryReportKind Kind => RegulatoryReportKind.PlacementResults;

    public async Task<Result<ComposedReport>> ComposeAsync(RegulatoryReport report, CancellationToken ct)
    {
        if (report.PropertyId is null)
            return Result.Failure<ComposedReport>(Error.Validation(
                "report.propertyRequired", "This notification is filed per issue."));

        var property = await _properties.GetByIdAsync(report.PropertyId.Value, ct);
        if (property is null)
            return Result.Failure<ComposedReport>(Error.NotFound("report.propertyNotFound", "Property not found."));

        var snapshot = (await _snapshots.ListByPropertyAsync(property.Id, ct))
            .Where(s => s.Purpose == SnapshotPurpose.Reporting && s.SnapshotAtUtc <= report.PeriodEndUtc)
            .MaxBy(s => s.SnapshotAtUtc);

        if (snapshot is null)
            return Result.Failure<ComposedReport>(Error.Conflict(
                "report.snapshotRequired",
                "Take a reporting snapshot of the holder register for this period before filing the results."));

        var placedTokens = property.TotalTokens - property.AvailableTokens;

        var content = new
        {
            reportKind = report.Kind.ToString(),
            basis = "Пункты 50 и 52 проекта Указа, форма приложения 6",
            issue = new
            {
                propertyId = property.Id,
                name = property.Name,
                registrationNumber = property.IssueRegistrationNumber,
                tokenContractAddress = property.TokenContractAddress,
                tokenChain = property.TokenChain,
                currency = property.Currency,
                tokenPrice = property.TokenPrice,
                totalTokens = property.TotalTokens
            },
            period = new { from = report.PeriodStartUtc, to = report.PeriodEndUtc },
            results = new
            {
                tokensPlaced = placedTokens,
                tokensUnplaced = property.AvailableTokens,
                amountPlaced = placedTokens * property.TokenPrice,
                holders = snapshot.AddressCount,
                tokensHeld = snapshot.TotalTokens
            },
            holderSnapshot = new
            {
                id = snapshot.Id,
                snapshotAtUtc = snapshot.SnapshotAtUtc,
                blockNumber = snapshot.BlockNumber
            }
        };

        return Result.Success(new ComposedReport(ReportJson.Serialize(content), snapshot.Id));
    }
}

/// <summary>
/// §15 (annex 8): the interim collateral report filed between audits. Reports the collateral file as
/// recorded plus its coverage of what is actually placed — the number the report exists to show.
/// </summary>
public sealed class CollateralInterimComposer : IRegulatoryReportComposer
{
    private readonly IPropertyRepository _properties;

    public CollateralInterimComposer(IPropertyRepository properties) => _properties = properties;

    public RegulatoryReportKind Kind => RegulatoryReportKind.CollateralInterim;

    public async Task<Result<ComposedReport>> ComposeAsync(RegulatoryReport report, CancellationToken ct)
    {
        if (report.PropertyId is null)
            return Result.Failure<ComposedReport>(Error.Validation(
                "report.propertyRequired", "This notification is filed per issue."));

        var property = await _properties.GetByIdAsync(report.PropertyId.Value, ct);
        if (property is null)
            return Result.Failure<ComposedReport>(Error.NotFound("report.propertyNotFound", "Property not found."));

        if (property.CollateralValue is null)
            return Result.Failure<ComposedReport>(Error.Conflict(
                "report.collateralMissing",
                "Record the appraisal of the collateral before filing the interim collateral report."));

        var placedTokens = property.TotalTokens - property.AvailableTokens;
        var placedValue = placedTokens * property.TokenPrice;

        var content = new
        {
            reportKind = report.Kind.ToString(),
            basis = "Пункт 15 проекта Указа, форма приложения 8",
            issue = new
            {
                propertyId = property.Id,
                name = property.Name,
                registrationNumber = property.IssueRegistrationNumber,
                currency = property.Currency
            },
            period = new { from = report.PeriodStartUtc, to = report.PeriodEndUtc },
            collateral = new
            {
                value = property.CollateralValue,
                valuedAtUtc = property.CollateralValuedAtUtc,
                appraiser = property.CollateralAppraiser,
                encumbranceRegistrationNumber = property.EncumbranceRegistrationNumber,
                encumbranceRegisteredAtUtc = property.EncumbranceRegisteredAtUtc,
                collateralManagerUserId = property.CollateralManagerUserId
            },
            coverage = new
            {
                placedTokens,
                placedValue,
                // Ratio of collateral to what is actually placed. Below 1 means the issue is not fully
                // backed — exactly what this report exists to surface.
                collateralToPlaced = placedValue == 0 ? (decimal?)null : property.CollateralValue / placedValue
            }
        };

        return Result.Success(new ComposedReport(ReportJson.Serialize(content)));
    }
}

/// <summary>
/// §80: the quarterly anti-money-laundering report. Built from what the platform itself observes —
/// onboarding, verification outcomes, blocked accounts and declined applications. Anything the
/// operator learns outside the platform is added to the filed document by hand.
/// </summary>
public sealed class AmlQuarterlyComposer : IRegulatoryReportComposer
{
    private readonly IUserRepository _users;
    private readonly IInvestmentRepository _investments;

    public AmlQuarterlyComposer(IUserRepository users, IInvestmentRepository investments)
    {
        _users = users;
        _investments = investments;
    }

    public RegulatoryReportKind Kind => RegulatoryReportKind.AmlQuarterly;

    public async Task<Result<ComposedReport>> ComposeAsync(RegulatoryReport report, CancellationToken ct)
    {
        var overview = await _users.GetOverviewAsync(ct);
        var investors = overview.Where(o => o.User.Role == Role.Investor).ToList();

        bool InPeriod(DateTime at) => at >= report.PeriodStartUtc && at <= report.PeriodEndUtc;

        var onboarded = investors.Count(o => InPeriod(o.User.CreatedAtUtc));
        var blocked = investors.Where(o => o.User.IsBanned).ToList();

        var rejected = (await _investments.ListAsync(InvestmentStatus.Rejected, null, 5_000, ct))
            .Where(i => InPeriod(i.UpdatedAtUtc ?? i.CreatedAtUtc))
            .ToList();

        var content = new
        {
            reportKind = report.Kind.ToString(),
            basis = "Пункт 80 проекта Указа",
            period = new { from = report.PeriodStartUtc, to = report.PeriodEndUtc },
            investors = new
            {
                total = investors.Count,
                onboardedInPeriod = onboarded,
                kycApproved = investors.Count(o => o.Kyc?.Status == KycStatus.Approved),
                kycRejected = investors.Count(o => o.Kyc?.Status == KycStatus.Rejected),
                kycPending = investors.Count(o => o.Kyc is null || o.Kyc.Status == KycStatus.Pending),
                blocked = blocked.Count
            },
            declinedApplications = new
            {
                count = rejected.Count,
                reasons = rejected
                    .Where(i => !string.IsNullOrWhiteSpace(i.RejectionReason))
                    .GroupBy(i => i.RejectionReason!)
                    .Select(g => new { reason = g.Key, count = g.Count() })
            },
            // Suspicious-activity findings are not derivable from platform data alone; the operator
            // completes this section before filing.
            suspiciousActivity = new { reported = (int?)null, notes = (string?)null }
        };

        return Result.Success(new ComposedReport(ReportJson.Serialize(content)));
    }
}
