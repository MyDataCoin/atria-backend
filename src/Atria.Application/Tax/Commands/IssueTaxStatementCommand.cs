using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Common;
using Atria.Domain.Kyc;
using Atria.Domain.Payouts;
using Atria.Domain.Tax;

namespace Atria.Application.Tax.Commands;

/// <summary>Issues the caller's income statement for a calendar year.</summary>
/// <param name="Year">The year the statement covers.</param>
public sealed record IssueTaxStatementCommand(int Year) : IRequest<Result<Guid>>;

/// <summary>
/// Builds the statement from the investor's own holdings and stores it as a numbered, verifiable
/// record. The figures come from the platform, not from whatever the page happened to be showing.
///
/// One statement per investor and year: asking twice returns the one already issued rather than
/// minting a second document with different numbers making the same claim. If the figures genuinely
/// change, that is a new year's statement or a corrected record — not a quietly different reprint.
/// </summary>
public sealed class IssueTaxStatementCommandHandler : IRequestHandler<IssueTaxStatementCommand, Result<Guid>>
{
    private readonly ITaxStatementRepository _statements;
    private readonly IInvestmentRepository _investments;
    private readonly IPayoutRunRepository _payouts;
    private readonly IKycRepository _kyc;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public IssueTaxStatementCommandHandler(
        ITaxStatementRepository statements,
        IInvestmentRepository investments,
        IPayoutRunRepository payouts,
        IKycRepository kyc,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _statements = statements;
        _investments = investments;
        _payouts = payouts;
        _kyc = kyc;
        _currentUser = currentUser;
        _clock = clock;
        _uow = uow;
    }

    private static readonly JsonSerializerOptions ContentJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<Result<Guid>> Handle(IssueTaxStatementCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<Guid>(
                Error.Unauthorized("taxStatement.unauthorized", "Authentication is required."));

        var now = _clock.UtcNow;
        // The current year is not over: a statement is issued once per year and then frozen, so
        // issuing it now would lock in a year without its remaining months.
        if (request.Year >= now.Year)
            return Result.Failure<Guid>(Error.Validation(
                "taxStatement.yearNotOver", "A statement can only be issued for a year that has ended."));

        var existing = await _statements.FindAsync(userId.Value, request.Year, ct);
        if (existing is not null)
            return Result.Success(existing.Id);

        // The name on a tax document has to be the verified one. Without approved KYC there is no
        // name the platform can stand behind, and a statement is not something to guess a name onto.
        var profile = await _kyc.GetByUserIdAsync(userId.Value, ct);
        if (profile is null || profile.Status != KycStatus.Approved || string.IsNullOrWhiteSpace(profile.FullName))
            return Result.Failure<Guid>(Error.Conflict(
                "taxStatement.kycRequired",
                "A statement can only be issued once identity verification is approved."));

        var holdings = await _investments.GetActiveHoldingsByInvestorAsync(userId.Value, ct);
        if (holdings.Count == 0)
            return Result.Failure<Guid>(Error.Conflict(
                "taxStatement.noHoldings", "There are no holdings to report for this investor."));

        var currency = holdings[0].Currency;
        var totalInvested = holdings.Sum(h => h.Amount);

        // Income is what was actually paid to this investor in the year: dividend lines recorded as
        // paid, dated by the payment, not by the register cut. A capital return is the investor's own
        // money coming back and is not income; a line still pending or failed was not received.
        var paidIncome = (await _payouts.ListForInvestorAsync(userId.Value, ct))
            .Where(x => x.Run.Kind == PayoutKind.Dividend
                        && x.Item.Status == PayoutItemStatus.Paid
                        && x.Item.PaidAtUtc is { } paidAt
                        && paidAt.Year == request.Year
                        && string.Equals(x.Run.Currency, currency, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Item.PaidAtUtc)
            .ToList();

        var totalIncome = paidIncome.Sum(x => x.Item.Amount);
        var holdingNames = holdings.GroupBy(h => h.PropertyId).ToDictionary(g => g.Key, g => g.First().PropertyName);

        // Relaxed escaping so the stored document reads as Cyrillic text rather than \uXXXX escapes:
        // this JSON is the statement's content, not a wire payload.
        var content = JsonSerializer.Serialize(new
        {
            year = request.Year,
            issuedAtUtc = now,
            investor = new { fullName = profile.FullName },
            totals = new { invested = totalInvested, income = totalIncome, currency },
            incomeNote = paidIncome.Count == 0
                ? "За отчётный период выплаты дохода не производились."
                : null,
            payouts = paidIncome.Select(x => new
            {
                propertyId = x.Run.PropertyId,
                propertyName = holdingNames.GetValueOrDefault(x.Run.PropertyId),
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

        TaxStatement statement;
        try
        {
            statement = TaxStatement.Issue(
                userId.Value, request.Year, profile.FullName!, totalInvested, totalIncome,
                currency, content, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(Error.Validation("taxStatement.invalid", ex.Message));
        }

        await _statements.AddAsync(statement, ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success(statement.Id);
    }
}
