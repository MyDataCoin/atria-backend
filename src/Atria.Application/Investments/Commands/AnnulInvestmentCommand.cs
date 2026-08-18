using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;
using Atria.Domain.Refunds;

namespace Atria.Application.Investments.Commands;

/// <summary>Voids an application that should not stand and returns its shares to the issue. Operator only.</summary>
/// <param name="InvestmentId">The application to annul.</param>
/// <param name="Reason">Why it is being voided; required and journalled.</param>
/// <param name="RecordRefund">
/// Whether money reached us for this application and is therefore owed back. Only the operator knows:
/// settlement happens off the platform, so the platform cannot work it out. Left on by default —
/// recording a debt that turns out not to exist is a line someone deletes, while missing one is money
/// an investor never sees again.
/// </param>
public sealed record AnnulInvestmentCommand(Guid InvestmentId, string Reason, bool RecordRefund = true)
    : IRequest<Result>;

/// <summary>
/// Unwinds an application the platform should not be carrying — a mistaken entry, a duplicate, or data
/// left over from testing.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that removing such a row is never done by hand in the database. A <c>DELETE</c> there
/// looks like it worked and quietly takes the shares with it: the reservation already decremented the
/// issue's available supply, and nothing puts it back. The issue then offers fewer shares than it has,
/// permanently, with no record of why.
/// </para>
/// <para>
/// The effects mirror a withdrawal, because an application that had been activated placed shares that
/// have to be unplaced: the shares go back on sale, the register stops counting them, anything minted
/// is burned, and — when money had actually been received — the refund is recorded. An application that
/// never activated placed nothing, so only the reservation is released.
/// </para>
/// </remarks>
public sealed class AnnulInvestmentCommandHandler : IRequestHandler<AnnulInvestmentCommand, Result>
{
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;
    private readonly IHolderPositionRepository _positions;
    private readonly IRefundObligationRepository _refunds;
    private readonly IComplianceRepository _profiles;
    private readonly IBlockchainOperationQueue _chain;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public AnnulInvestmentCommandHandler(
        IInvestmentRepository investments,
        IPropertyRepository properties,
        IHolderPositionRepository positions,
        IRefundObligationRepository refunds,
        IComplianceRepository profiles,
        IBlockchainOperationQueue chain,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _investments = investments;
        _properties = properties;
        _positions = positions;
        _refunds = refunds;
        _profiles = profiles;
        _chain = chain;
        _clock = clock;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(AnnulInvestmentCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure(Error.Validation("investment.reasonRequired", "An annulment reason is required."));

        var investment = await _investments.GetByIdAsync(request.InvestmentId, ct);
        if (investment is null)
            return Result.Failure(Error.NotFound("investment.notFound", "Investment not found."));

        // Read before the transition: afterwards the status is Annulled and no longer says whether
        // anything had actually been placed.
        var wasActive = investment.Status == InvestmentStatus.Active;

        try
        {
            investment.Annul(request.Reason);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("investment.notAnnullable", ex.Message));
        }

        _investments.Update(investment);

        var property = await _properties.GetByIdAsync(investment.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("investment.propertyNotFound", "Property not found."));

        // The whole point: the shares go back on sale. Reserved or Active, they were held out of the
        // pool either way.
        property.ReleaseTokens(investment.TokenCount);
        _properties.Update(property);

        var profile = await _profiles.GetByInvestorAsync(investment.InvestorId, ct);
        var wallet = investment.WalletAddress ?? profile?.WalletAddress;

        if (wasActive)
        {
            if (request.RecordRefund)
                await _refunds.AddAsync(RefundObligation.Raise(
                    property.Id, investment.InvestorId, wallet ?? "—", investment.TokenCount,
                    investment.Amount, investment.Currency, RefundReason.IssueAnnulled,
                    $"Заявка {investment.Id} аннулирована оператором: {request.Reason}"), ct);

            if (!string.IsNullOrWhiteSpace(wallet))
            {
                await StopCountingInRegistryAsync(property.Id, wallet, investment.TokenCount, ct);

                // Only what actually reached the address needs burning.
                if (investment.OnChainStatus != OnChainStatus.None
                    && !string.IsNullOrWhiteSpace(property.TokenContractAddress))
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        propertyId = property.Id,
                        chain = property.TokenChain,
                        tokenContractAddress = property.TokenContractAddress,
                        holder = wallet,
                        amount = investment.TokenCount,
                        reason = "AnnulledByOperator"
                    });

                    await _chain.EnqueueAsync(
                        BlockchainOperationType.TokenBurn, payload,
                        $"token-burn:annulment:{investment.Id}", ct);
                }
            }
        }

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.InvestmentAnnulled,
            $"Заявка на {investment.TokenCount} долей объекта «{property.Name}» аннулирована оператором: "
            + request.Reason,
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Takes the shares out of the holder's position so the register stops counting them.</summary>
    private async Task StopCountingInRegistryAsync(
        Guid propertyId, string wallet, decimal tokenCount, CancellationToken ct)
    {
        var position = await _positions.GetByAddressAsync(propertyId, wallet, ct);
        if (position is null)
            return;

        position.Sync(Math.Max(position.TokenCount - tokenCount, 0), position.Source, _clock.UtcNow);
        _positions.Update(position);
    }
}
