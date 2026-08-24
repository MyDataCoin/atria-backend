using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;
using Atria.Domain.Refunds;
using Atria.Domain.Whitelist;

namespace Atria.Application.Investments.Commands;

/// <summary>
/// The holder exercises the 14-day right of withdrawal (draft Decree, §44). Investor only, on their
/// own investment.
/// </summary>
/// <param name="InvestmentId">The investment to withdraw from.</param>
public sealed record WithdrawInvestmentCommand(Guid InvestmentId) : IRequest<Result>;

/// <summary>
/// Unwinds an activated purchase inside its withdrawal window: the shares go back to the issue,
/// anything already minted is burned off the holder's address, the registry stops counting it, and
/// the money owed back is recorded.
/// </summary>
/// <remarks>
/// <para>
/// §44 gives the holder 14 calendar days from the agreement, with no reason required and no penalty.
/// The four effects go together: returning the shares without recording the refund would leave the
/// holder with neither; recording the refund without burning would leave them holding shares they
/// have been paid out of.
/// </para>
/// <para>
/// Paying the refund is a separate step, as everywhere else — how funds reach an investor is decided
/// outside the platform. What exists from this moment is the obligation.
/// </para>
/// </remarks>
public sealed class WithdrawInvestmentCommandHandler : IRequestHandler<WithdrawInvestmentCommand, Result>
{
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;
    private readonly IHolderPositionRepository _positions;
    private readonly IRefundObligationRepository _refunds;
    private readonly IComplianceRepository _profiles;
    private readonly IWhitelistEntryRepository _whitelist;
    private readonly IBlockchainOperationQueue _chain;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public WithdrawInvestmentCommandHandler(
        IInvestmentRepository investments,
        IPropertyRepository properties,
        IHolderPositionRepository positions,
        IRefundObligationRepository refunds,
        IComplianceRepository profiles,
        IWhitelistEntryRepository whitelist,
        IBlockchainOperationQueue chain,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _investments = investments;
        _properties = properties;
        _positions = positions;
        _refunds = refunds;
        _profiles = profiles;
        _whitelist = whitelist;
        _chain = chain;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(WithdrawInvestmentCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure(Error.Unauthorized("investment.unauthorized", "Authentication is required."));

        var investment = await _investments.GetByIdAsync(request.InvestmentId, ct);

        // Someone else's investment answers not-found: whether a given id exists is not the caller's
        // business.
        if (investment is null || investment.InvestorId != userId.Value)
            return Result.Failure(Error.NotFound("investment.notFound", "Investment not found."));

        var now = _clock.UtcNow;

        try
        {
            investment.Withdraw(now);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("investment.withdrawalClosed", ex.Message));
        }

        _investments.Update(investment);

        var property = await _properties.GetByIdAsync(investment.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("investment.propertyNotFound", "Property not found."));

        // The shares go back on sale.
        property.ReleaseTokens(investment.TokenCount);
        _properties.Update(property);

        // The address the shares actually went to. The whitelist request snapshots it at mint time,
        // so a profile whose wallet changed afterwards cannot send the burn to the wrong holder.
        var entry = await _whitelist.GetByInvestmentAsync(investment.Id, ct);
        var profile = await _profiles.GetByInvestorAsync(investment.InvestorId, ct);
        var wallet = entry?.WalletAddress ?? investment.WalletAddress ?? profile?.WalletAddress;

        // What is owed back, at the price paid. A withdrawal unwinds the purchase; it does not
        // revalue it, so the holder is neither penalised nor enriched by the last two weeks.
        await _refunds.AddAsync(RefundObligation.Raise(
            property.Id, investment.InvestorId, wallet ?? "—", investment.TokenCount,
            investment.Amount, investment.Currency, RefundReason.WithdrawalWithin14Days,
            $"Отказ в течение 14 дней по заявке {investment.Id}"), ct);

        if (!string.IsNullOrWhiteSpace(wallet))
        {
            await StopCountingInRegistryAsync(property.Id, wallet, investment.TokenCount, now, ct);

            // Only what actually reached the address needs burning. An application activated but
            // never minted has nothing on chain to undo. Either witness is enough: the platform's own
            // allocation writes OnChainStatus, a batch handed to the exchange only marks the request
            // Minted — reading just one of them leaves the other kind of mint un-burned.
            if ((investment.OnChainStatus != OnChainStatus.None
                 || entry?.Status == WhitelistStatus.Minted)
                && !string.IsNullOrWhiteSpace(property.TokenContractAddress))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    propertyId = property.Id,
                    chain = property.TokenChain,
                    tokenContractAddress = property.TokenContractAddress,
                    holder = wallet,
                    amount = investment.TokenCount,
                    reason = "WithdrawalWithin14Days"
                });

                await _chain.EnqueueAsync(
                    Domain.Compliance.BlockchainOperationType.TokenBurn, payload,
                    $"token-burn:withdrawal:{investment.Id}", ct);
            }
        }

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.InvestmentWithdrawn,
            $"Инвестор отказался от {investment.TokenCount} долей объекта «{property.Name}» "
            + $"в течение 14 дней; к возврату {investment.Amount} {investment.Currency}",
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Takes the withdrawn shares out of the holder's position so the register stops counting them.
    /// The burn on chain confirms it later; until then the two would disagree, and the reconciliation
    /// is what surfaces that rather than the register quietly being ahead.
    /// </summary>
    private async Task StopCountingInRegistryAsync(
        Guid propertyId, string wallet, long tokenCount, DateTime now, CancellationToken ct)
    {
        var position = await _positions.GetByAddressAsync(propertyId, wallet, ct);
        if (position is null)
            return;

        position.Sync(Math.Max(position.TokenCount - tokenCount, 0), position.Source, now);
        _positions.Update(position);
    }
}
