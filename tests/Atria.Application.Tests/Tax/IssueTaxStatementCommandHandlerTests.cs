using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Tax.Commands;
using Atria.Domain.Holders;
using Atria.Domain.Kyc;
using Atria.Domain.Payouts;
using Atria.Domain.Tax;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Tax;

/// <summary>
/// The statement is what an investor hands to the tax office, so it is issued from platform records
/// under a verified name — never assembled from whatever a page was showing.
/// </summary>
public sealed class IssueTaxStatementCommandHandlerTests
{
    private readonly ITaxStatementRepository _statements = Substitute.For<ITaxStatementRepository>();
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly IPayoutRunRepository _payouts = Substitute.For<IPayoutRunRepository>();
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid InvestorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    // The statement year (2026) has ended by the time it is issued.
    private static readonly DateTime Now = new(2027, 2, 4, 10, 0, 0, DateTimeKind.Utc);

    private readonly List<TaxStatement> _issued = new();

    public IssueTaxStatementCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _currentUser.UserId.Returns(InvestorId);
        _statements.When(r => r.AddAsync(Arg.Any<TaxStatement>(), Arg.Any<CancellationToken>()))
            .Do(c => _issued.Add(c.Arg<TaxStatement>()));
    }

    private IssueTaxStatementCommandHandler NewHandler() =>
        new(_statements, _investments, _payouts, _kyc, _currentUser, _clock, _uow);

    // One distribution to this investor, approved and — when paidAt is given — recorded as paid then.
    private (PayoutRun Run, PayoutItem Item) PayoutLine(
        PayoutKind kind, decimal amount, DateTime? paidAt, string currency = "KGS")
    {
        var snapshot = HolderSnapshot.Create(
            Guid.NewGuid(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), SnapshotPurpose.Payout, 100,
            Guid.NewGuid(), [new HolderSnapshotEntry("0xaaa", 100, InvestorId)]);
        var run = PayoutRun.Create(snapshot, kind, PayoutMethod.BankTransfer, amount, currency, Guid.NewGuid(), null);
        var item = run.Items.Single();
        if (paidAt is { } at)
        {
            run.Approve(at.AddDays(-1));
            run.SettleItem(item.Id, "PP-1", at);
        }
        return (run, item);
    }

    private void GivenApprovedKyc(string fullName = "Иванов Иван Иванович")
    {
        var profile = KycProfile.Create(InvestorId);
        profile.Submit(KycProviderType.Manual, "session", null, null, fullName, null, null);
        profile.Approve();
        _kyc.GetByUserIdAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(profile);
    }

    private void GivenHoldings(
        params (Guid PropertyId, string Name, long Tokens, decimal Amount, long TotalTokens)[] holdings)
        => _investments.GetHoldingsAtAsync(InvestorId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(holdings
                .Select(h => (h.PropertyId, h.Name, h.Tokens, h.Amount, "KGS", h.TotalTokens))
                .ToList());

    [Fact]
    public async Task A_statement_is_issued_from_the_investors_own_holdings()
    {
        GivenApprovedKyc();
        GivenHoldings(
            (Guid.NewGuid(), "Tower One", 30, 3_000m, 1_000),
            (Guid.NewGuid(), "Tower Two", 10, 1_000m, 500));

        var result = await NewHandler().Handle(new IssueTaxStatementCommand(2026), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var statement = _issued.Single();
        statement.Year.Should().Be(2026);
        statement.TotalInvested.Should().Be(4_000m);
        statement.InvestorFullName.Should().Be("Иванов Иван Иванович");
        statement.Number.Should().StartWith("ATRIA-2026-");
        statement.VerificationCode.Should().HaveLength(20);
        statement.Content.Should().Contain("Tower One").And.Contain("Tower Two");
    }

    [Fact]
    public async Task Income_is_the_dividends_actually_paid_in_that_year()
    {
        GivenApprovedKyc();
        GivenHoldings((Guid.NewGuid(), "Tower One", 30, 3_000m, 1_000));
        _payouts.ListForInvestorAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            PayoutLine(PayoutKind.Dividend, 120m, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)),
            PayoutLine(PayoutKind.Dividend, 80m, new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)),
            // Paid the next year, a return of capital, and a line never paid: none of them is 2026 income.
            PayoutLine(PayoutKind.Dividend, 500m, new DateTime(2027, 1, 20, 0, 0, 0, DateTimeKind.Utc)),
            PayoutLine(PayoutKind.CapitalReturn, 1_000m, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
            PayoutLine(PayoutKind.Dividend, 70m, null),
        });

        await NewHandler().Handle(new IssueTaxStatementCommand(2026), CancellationToken.None);

        var statement = _issued.Single();
        statement.TotalIncome.Should().Be(200m);
        statement.Content.Should().NotContain("выплаты дохода не производились");
    }

    /// <summary>With nothing paid in the year the statement says zero rather than leaving it blank.</summary>
    [Fact]
    public async Task Income_is_reported_as_zero_with_an_explicit_note()
    {
        GivenApprovedKyc();
        GivenHoldings((Guid.NewGuid(), "Tower One", 30, 3_000m, 1_000));

        await NewHandler().Handle(new IssueTaxStatementCommand(2026), CancellationToken.None);

        var statement = _issued.Single();
        statement.TotalIncome.Should().Be(0m);
        statement.Content.Should().Contain("выплаты дохода не производились");
    }

    [Fact]
    public async Task Asking_twice_for_a_year_returns_the_statement_already_issued()
    {
        GivenApprovedKyc();
        GivenHoldings((Guid.NewGuid(), "Tower One", 30, 3_000m, 1_000));

        var existing = TaxStatement.Issue(
            InvestorId, 2026, "Иванов Иван Иванович", 3_000m, 0m, "KGS", "{}", Now);
        _statements.FindAsync(InvestorId, 2026, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await NewHandler().Handle(new IssueTaxStatementCommand(2026), CancellationToken.None);

        result.Value.Should().Be(existing.Id);
        _issued.Should().BeEmpty();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_approved_verification_no_statement_is_issued()
    {
        var profile = KycProfile.Create(InvestorId);
        profile.Submit(KycProviderType.Manual, "session", null, null, "Иванов Иван Иванович", null, null);
        _kyc.GetByUserIdAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(profile); // still pending
        GivenHoldings((Guid.NewGuid(), "Tower One", 30, 3_000m, 1_000));

        var result = await NewHandler().Handle(new IssueTaxStatementCommand(2026), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _issued.Should().BeEmpty();
    }

    [Fact]
    public async Task An_investor_with_no_holdings_has_nothing_to_report()
    {
        GivenApprovedKyc();
        GivenHoldings();

        var result = await NewHandler().Handle(new IssueTaxStatementCommand(2026), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task A_statement_cannot_be_issued_for_a_year_that_has_not_happened()
    {
        GivenApprovedKyc();
        GivenHoldings((Guid.NewGuid(), "Tower One", 30, 3_000m, 1_000));

        var result = await NewHandler().Handle(new IssueTaxStatementCommand(2027), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task An_unauthenticated_caller_gets_nothing()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await NewHandler().Handle(new IssueTaxStatementCommand(2026), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    /// <summary>Two statements must not share a verification code — it is what proves a document.</summary>
    [Fact]
    public void Verification_codes_do_not_repeat()
    {
        var codes = Enumerable.Range(0, 200)
            .Select(_ => TaxStatement.Issue(
                Guid.NewGuid(), 2026, "Иванов Иван Иванович", 1m, 0m, "KGS", "{}", Now).VerificationCode)
            .ToList();

        codes.Distinct().Should().HaveCount(codes.Count);
    }

    [Fact]
    public async Task The_current_year_cannot_be_issued_before_it_ends()
    {
        var result = await NewHandler().Handle(new IssueTaxStatementCommand(2027), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("taxStatement.yearNotOver");
    }

    [Fact]
    public async Task Holdings_are_read_as_they_stood_at_the_end_of_the_year()
    {
        GivenApprovedKyc();
        GivenHoldings((Guid.NewGuid(), "Tower One", 30, 3_000m, 1_000));

        await NewHandler().Handle(new IssueTaxStatementCommand(2026), CancellationToken.None);

        // The cutoff is the first instant of the next year, not "now" (February 2027).
        await _investments.Received(1).GetHoldingsAtAsync(
            InvestorId, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Income_is_reported_even_when_everything_was_sold_during_the_year()
    {
        GivenApprovedKyc();
        GivenHoldings();
        _payouts.ListForInvestorAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            PayoutLine(PayoutKind.Dividend, 50m, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
        });

        var result = await NewHandler().Handle(new IssueTaxStatementCommand(2026), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _issued.Single().TotalIncome.Should().Be(50m);
        _issued.Single().TotalInvested.Should().Be(0m);
    }
}
