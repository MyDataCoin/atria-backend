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
    private readonly IBurnCapability _burn;
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
        IBurnCapability burn,
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
        _burn = burn;
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

        var property = await _properties.GetByIdAsync(investment.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("investment.propertyNotFound", "Property not found."));

        // The address the shares actually went to. The whitelist request snapshots it at mint time,
        // so a profile whose wallet changed afterwards cannot send the burn to the wrong holder.
        var entry = await _whitelist.GetByInvestmentAsync(investment.Id, ct);
        var profile = await _profiles.GetByInvestorAsync(investment.InvestorId, ct);
        var wallet = entry?.WalletAddress ?? investment.WalletAddress ?? profile?.WalletAddress;

        var burnRequired = !string.IsNullOrWhiteSpace(wallet) && NeedsBurn(investment, entry, property);

        // Asked BEFORE the withdrawal is accepted, and nothing above this line has changed anything.
        // The gateway raises the same gap, but only once the investment is marked withdrawn and the
        // refund obligation exists — leaving the investor holding both the money back and the shares,
        // and the register disagreeing with the chain until a reconciliation notices. Refusing the
        // whole withdrawal is the lesser failure: the holder still has their shares and their §44
        // window, and can exercise it again once the key is configured.
        if (burnRequired && !_burn.CanBurn)
        {
            return Result.Failure(Error.Conflict(
                "investment.burnUnavailable",
                "Отказ от сделки временно недоступен: платформа не может погасить уже выпущенные "
                + "доли. Обратитесь в поддержку — заявка не изменена."));
        }

        try
        {
            investment.Withdraw(now);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("investment.withdrawalClosed", ex.Message));
        }

        _investments.Update(investment);

        // The shares go back on sale.
        property.ReleaseTokens(investment.TokenCount);
        _properties.Update(property);

        // What is owed back, at the price paid. A withdrawal unwinds the purchase; it does not
        // revalue it, so the holder is neither penalised nor enriched by the last two weeks.
        await _refunds.AddAsync(RefundObligation.Raise(
            property.Id, investment.InvestorId, wallet ?? "—", investment.TokenCount,
            investment.Amount, investment.Currency, RefundReason.WithdrawalWithin14Days,
            $"Отказ в течение 14 дней по заявке {investment.Id}"), ct);

        if (!string.IsNullOrWhiteSpace(wallet))
        {
            await StopCountingInRegistryAsync(property.Id, wallet, investment.TokenCount, now, ct);

            if (burnRequired)
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
    /// Whether shares actually reached the chain and so have to be destroyed there.
    /// </summary>
    /// <remarks>
    /// An application activated but never minted has nothing on chain to undo. Either witness is
    /// enough: the platform's own allocation writes <see cref="OnChainStatus"/>, a batch handed to the
    /// exchange only marks the request <see cref="WhitelistStatus.Minted"/> — reading just one of them
    /// leaves the other kind of mint un-burned. Decided once and used twice, so the burn the handler
    /// refuses over is exactly the burn it would otherwise queue.
    /// </remarks>
    private static bool NeedsBurn(Investment investment, WhitelistEntry? entry, Property property)
        => (investment.OnChainStatus != OnChainStatus.None || entry?.Status == WhitelistStatus.Minted)
           && !string.IsNullOrWhiteSpace(property.TokenContractAddress);

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
