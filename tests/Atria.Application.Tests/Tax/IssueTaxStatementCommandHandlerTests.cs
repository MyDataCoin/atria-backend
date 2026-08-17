using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Tax.Commands;
using Atria.Domain.Kyc;
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
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid InvestorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now = new(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);

    private readonly List<TaxStatement> _issued = new();

    public IssueTaxStatementCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _currentUser.UserId.Returns(InvestorId);
        _statements.When(r => r.AddAsync(Arg.Any<TaxStatement>(), Arg.Any<CancellationToken>()))
            .Do(c => _issued.Add(c.Arg<TaxStatement>()));
    }

    private IssueTaxStatementCommandHandler NewHandler() =>
        new(_statements, _investments, _kyc, _currentUser, _clock, _uow);

    private void GivenApprovedKyc(string fullName = "Иванов Иван Иванович")
    {
        var profile = KycProfile.Create(InvestorId);
        profile.Submit(KycProviderType.Manual, "session", null, null, fullName, null, null);
        profile.Approve();
        _kyc.GetByUserIdAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(profile);
    }

    private void GivenHoldings(
        params (Guid PropertyId, string Name, decimal Tokens, decimal Amount, decimal TotalTokens)[] holdings)
        => _investments.GetActiveHoldingsByInvestorAsync(InvestorId, Arg.Any<CancellationToken>())
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

    /// <summary>No payout module exists yet, so the statement says zero rather than leaving it blank.</summary>
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
}
