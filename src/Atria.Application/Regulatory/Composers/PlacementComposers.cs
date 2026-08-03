using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Investments;
using Atria.Domain.Regulatory;

namespace Atria.Application.Regulatory.Composers;

/// <summary>Shared JSON settings so every generated document reads the same way.</summary>
internal static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

/// <summary>
/// §24: funds landed on the issuer's account and the matching number of tokens was issued. Covers the
/// placements activated within the period — activation is the moment the platform records the tokens
/// as issued to an investor.
/// </summary>
public sealed class FundsReceivedAndIssuedComposer : IRegulatoryReportComposer
{
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;

    public FundsReceivedAndIssuedComposer(IInvestmentRepository investments, IPropertyRepository properties)
    {
        _investments = investments;
        _properties = properties;
    }

    public RegulatoryReportKind Kind => RegulatoryReportKind.FundsReceivedAndIssued;

    public async Task<Result<ComposedReport>> ComposeAsync(RegulatoryReport report, CancellationToken ct)
    {
        if (report.PropertyId is null)
            return Result.Failure<ComposedReport>(Error.Validation(
                "report.propertyRequired", "This notification is filed per issue."));

        var property = await _properties.GetByIdAsync(report.PropertyId.Value, ct);
        if (property is null)
            return Result.Failure<ComposedReport>(Error.NotFound("report.propertyNotFound", "Property not found."));

        var activated = (await _investments.ListAsync(InvestmentStatus.Active, property.Id, 5_000, ct))
            .Where(i => i.ActivatedAtUtc is not null
                && i.ActivatedAtUtc >= report.PeriodStartUtc
                && i.ActivatedAtUtc <= report.PeriodEndUtc)
            .ToList();

        var content = new
        {
            reportKind = report.Kind.ToString(),
            basis = "Пункт 24 проекта Указа",
            issue = new
            {
                propertyId = property.Id,
                name = property.Name,
                registrationNumber = property.IssueRegistrationNumber,
                tokenContractAddress = property.TokenContractAddress,
                tokenChain = property.TokenChain,
                issuerWalletAddress = property.IssuerWalletAddress
            },
            period = new { from = report.PeriodStartUtc, to = report.PeriodEndUtc },
            totals = new
            {
                placements = activated.Count,
                tokensIssued = activated.Sum(i => i.TokenCount),
                fundsReceived = activated.Sum(i => i.Amount),
                currency = property.Currency
            },
            placements = activated.Select(i => new
            {
                investmentId = i.Id,
                investorId = i.InvestorId,
                activatedAtUtc = i.ActivatedAtUtc,
                tokens = i.TokenCount,
                amount = i.Amount,
                walletAddress = i.WalletAddress,
                transactionHash = i.TransactionHash,
                onChainStatus = i.OnChainStatus.ToString()
            })
        };

        return Result.Success(new ComposedReport(ReportJson.Serialize(content)));
    }
}

/// <summary>
/// §49: the amount actually placed over the month and the registration fee due on it. The fee rate is
/// not in the platform — it is set by the regulator — so the base is reported and the fee is left to
/// be filled in rather than guessed at.
/// </summary>
public sealed class MonthlyPlacementComposer : IRegulatoryReportComposer
{
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;

    public MonthlyPlacementComposer(IInvestmentRepository investments, IPropertyRepository properties)
    {
        _investments = investments;
        _properties = properties;
    }

    public RegulatoryReportKind Kind => RegulatoryReportKind.MonthlyPlacement;

    public async Task<Result<ComposedReport>> ComposeAsync(RegulatoryReport report, CancellationToken ct)
    {
        if (report.PropertyId is null)
            return Result.Failure<ComposedReport>(Error.Validation(
                "report.propertyRequired", "This notification is filed per issue."));

        var property = await _properties.GetByIdAsync(report.PropertyId.Value, ct);
        if (property is null)
            return Result.Failure<ComposedReport>(Error.NotFound("report.propertyNotFound", "Property not found."));

        var activated = (await _investments.ListAsync(InvestmentStatus.Active, property.Id, 5_000, ct))
            .Where(i => i.ActivatedAtUtc is not null
                && i.ActivatedAtUtc >= report.PeriodStartUtc
                && i.ActivatedAtUtc <= report.PeriodEndUtc)
            .ToList();

        var placedAmount = activated.Sum(i => i.Amount);

        var content = new
        {
            reportKind = report.Kind.ToString(),
            basis = "Пункт 49 проекта Указа",
            issue = new
            {
                propertyId = property.Id,
                name = property.Name,
                registrationNumber = property.IssueRegistrationNumber
            },
            period = new { from = report.PeriodStartUtc, to = report.PeriodEndUtc },
            placement = new
            {
                placements = activated.Count,
                tokensPlaced = activated.Sum(i => i.TokenCount),
                amountPlaced = placedAmount,
                currency = property.Currency
            },
            registrationFee = new
            {
                baseAmount = placedAmount,
                // Rate and payment reference come from the regulator, not from platform data.
                ratePercent = (decimal?)null,
                amount = (decimal?)null,
                paymentReference = (string?)null
            }
        };

        return Result.Success(new ComposedReport(ReportJson.Serialize(content)));
    }
}
