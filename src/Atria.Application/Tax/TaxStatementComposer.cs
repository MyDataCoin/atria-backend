using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Kyc;
using Atria.Domain.Payouts;

namespace Atria.Application.Tax;

/// <summary>What a statement says, before it is numbered and stored (or shown as a draft).</summary>
/// <param name="FullName">The verified name the statement is made out to.</param>
/// <param name="TotalInvested">What was paid for the holdings held at the cut-off.</param>
/// <param name="TotalIncome">Income actually paid in the year up to the cut-off.</param>
/// <param name="Currency">Currency of both totals.</param>
/// <param name="AsOfUtc">The instant holdings are read at: the end of the year, or now for a year still running.</param>
/// <param name="Content">The statement body as JSON — holdings, payout lines, totals.</param>
public sealed record TaxStatementComposition(
    string FullName, decimal TotalInvested, decimal TotalIncome, string Currency, DateTime AsOfUtc, string Content);

/// <summary>
/// Builds a statement's figures from platform records. Shared by issuing (which freezes the result
/// under a number) and by the draft view (which shows the same figures for a year still running), so
/// a draft can never say something the issued statement would not.
/// </summary>
public sealed class TaxStatementComposer
{
    private static readonly JsonSerializerOptions ContentJson = new()
    {
        // Relaxed escaping so the stored document reads as Cyrillic text rather than \uXXXX escapes:
        // this JSON is the statement's content, not a wire payload.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IInvestmentRepository _investments;
    private readonly IPayoutRunRepository _payouts;
    private readonly IPropertyRepository _properties;
    private readonly IKycRepository _kyc;

    public TaxStatementComposer(
        IInvestmentRepository investments,
        IPayoutRunRepository payouts,
        IPropertyRepository properties,
        IKycRepository kyc)
    {
        _investments = investments;
        _payouts = payouts;
        _properties = properties;
        _kyc = kyc;
    }

    public async Task<Result<TaxStatementComposition>> ComposeAsync(
        Guid investorId, int year, DateTime nowUtc, CancellationToken ct)
    {
        // The name on a tax document has to be the verified one. Without approved KYC there is no
        // name the platform can stand behind, and a statement is not something to guess a name onto.
        var profile = await _kyc.GetByUserIdAsync(investorId, ct);
        if (profile is null || profile.Status != KycStatus.Approved || string.IsNullOrWhiteSpace(profile.FullName))
            return Result.Failure<TaxStatementComposition>(Error.Conflict(
                "taxStatement.kycRequired",
                "A statement can only be issued once identity verification is approved."));

        // Holdings as they stood at the end of the year, not as they are today: a stake bought after
        // the year closed was not held in it, and one sold after it still was. For a year still
        // running (a draft) the end has not come yet, so the cut-off is now.
        var yearEnd = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cutoff = nowUtc < yearEnd ? nowUtc : yearEnd;
        var holdings = await _investments.GetHoldingsAtAsync(investorId, cutoff, ct);
        var lines = await _payouts.ListForInvestorAsync(investorId, ct);

        var currency = holdings.Count > 0 ? holdings[0].Currency : lines.FirstOrDefault().Run?.Currency ?? "KGS";
        var totalInvested = holdings.Sum(h => h.Amount);

        // Income is what was actually paid to this investor in the year: dividend lines recorded as
        // paid, dated by the payment, not by the register cut. A capital return is the investor's own
        // money coming back and is not income; a line still pending or failed was not received.
        var paidIncome = lines
            .Where(x => x.Run.Kind == PayoutKind.Dividend
                        && x.Item.Status == PayoutItemStatus.Paid
                        && x.Item.PaidAtUtc is { } paidAt
                        && paidAt.Year == year
                        && paidAt < cutoff
                        && string.Equals(x.Run.Currency, currency, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Item.PaidAtUtc)
            .ToList();

        var totalIncome = paidIncome.Sum(x => x.Item.Amount);

        // Someone who sold everything during the year holds nothing at its end but may still have
        // been paid income in it — that still has to be reported.
        if (holdings.Count == 0 && paidIncome.Count == 0)
            return Result.Failure<TaxStatementComposition>(Error.Conflict(
                "taxStatement.noHoldings", "There are no holdings or income to report for this year."));

        // Every payout line names its property. Most are among the year-end holdings; one sold
        // during the year is not, so its name comes from the property itself.
        var propertyNames = holdings.GroupBy(h => h.PropertyId).ToDictionary(g => g.Key, g => g.First().PropertyName);
        foreach (var propertyId in paidIncome.Select(x => x.Run.PropertyId).Distinct())
        {
            if (propertyNames.ContainsKey(propertyId)) continue;
            var property = await _properties.GetByIdAsync(propertyId, ct);
            if (property is not null)
                propertyNames[propertyId] = property.Name;
        }

        var asOf = cutoff.AddTicks(-1);
        var content = JsonSerializer.Serialize(new
        {
            year,
            issuedAtUtc = nowUtc,
            investor = new { fullName = profile.FullName },
            totals = new { invested = totalInvested, income = totalIncome, currency },
            holdingsAsOfUtc = asOf,
            incomeNote = paidIncome.Count == 0
                ? "За отчётный период выплаты дохода не производились."
                : null,
            payouts = paidIncome.Select(x => new
            {
                propertyId = x.Run.PropertyId,
                propertyName = propertyNames.GetValueOrDefault(x.Run.PropertyId),
                paidAtUtc = x.Item.PaidAtUtc,
                tokens = x.Item.TokenCount,
                amount = x.Item.Amount,
                currency = x.Run.Currency,
                reference = x.Item.SettlementReference
            }),
            holdings = holdings.Select(h => new
            {
                propertyId = h.PropertyId,
                propertyName = h.PropertyName,
                tokens = h.TokenCount,
                invested = h.Amount,
                currency = h.Currency,
                shareOfIssue = h.TotalTokens == 0 ? 0m : (decimal)h.TokenCount / h.TotalTokens
            })
        }, ContentJson);

        return Result.Success(new TaxStatementComposition(
            profile.FullName!, totalInvested, totalIncome, currency, asOf, content));
    }
}
