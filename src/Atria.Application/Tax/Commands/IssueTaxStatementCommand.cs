using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Common;
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
    private readonly TaxStatementComposer _composer;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public IssueTaxStatementCommandHandler(
        ITaxStatementRepository statements,
        IInvestmentRepository investments,
        IPayoutRunRepository payouts,
        IPropertyRepository properties,
        IKycRepository kyc,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _statements = statements;
        _composer = new TaxStatementComposer(investments, payouts, properties, kyc);
        _currentUser = currentUser;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<Guid>> Handle(IssueTaxStatementCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<Guid>(
                Error.Unauthorized("taxStatement.unauthorized", "Authentication is required."));

        var now = _clock.UtcNow;
        // The current year is not over: a statement is issued once per year and then frozen, so
        // issuing it now would lock in a year without its remaining months. The draft view shows a
        // running year without freezing anything.
        if (request.Year >= now.Year)
            return Result.Failure<Guid>(Error.Validation(
                "taxStatement.yearNotOver", "A statement can only be issued for a year that has ended."));

        var existing = await _statements.FindAsync(userId.Value, request.Year, ct);
        if (existing is not null)
            return Result.Success(existing.Id);

        var composed = await _composer.ComposeAsync(userId.Value, request.Year, now, ct);
        if (composed.IsFailure)
            return Result.Failure<Guid>(composed.Error);

        var c = composed.Value;
        TaxStatement statement;
        try
        {
            statement = TaxStatement.Issue(
                userId.Value, request.Year, c.FullName, c.TotalInvested, c.TotalIncome,
                c.Currency, c.Content, now);
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
