using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.TravelRule;
using Atria.Domain.Holders;

namespace Atria.Application.Holders.Commands;

/// <summary>Replays an issue's transfers from the chain into the holder registry.</summary>
/// <param name="PropertyId">The issue to sync.</param>
public sealed record SyncHolderRegistryFromChainCommand(Guid PropertyId) : IRequest<Result<int>>;

/// <summary>
/// Advances the registry to match the chain by applying transfer events, and makes the chain the
/// source of truth for who holds what.
/// </summary>
/// <remarks>
/// <para>
/// Until this runs, the registry is a projection of our own records — what the platform believed
/// should happen. That is fine while the platform is the only thing that moves shares, and wrong the
/// moment two investors trade with each other: nobody tells the backend, and the register silently
/// stops matching reality. Replaying <c>Transfer</c> events fixes that, because a mint, a burn and a
/// trade all appear as the same event.
/// </para>
/// <para>
/// Only blocks deep enough to be final are applied. Reading up to the head would mean applying a
/// transfer that a reorg then removes, and a registry that has to be walked backwards cannot be
/// snapshotted for a payout.
/// </para>
/// </remarks>
public sealed class SyncHolderRegistryFromChainCommandHandler
    : IRequestHandler<SyncHolderRegistryFromChainCommand, Result<int>>
{
    /// <summary>Blocks left untouched at the tip, matching the depth used to confirm operations.</summary>
    private const int ConfirmationDepth = 15;

    /// <summary>Blocks read per run, so a long history drains across passes instead of one huge query.</summary>
    private const int MaxBlocksPerRun = 5_000;

    private const string ZeroAddress = "0x0000000000000000000000000000000000000000";

    private readonly IPropertyRepository _properties;
    private readonly IHolderPositionRepository _positions;
    private readonly IChainSyncCursorRepository _cursors;
    private readonly IComplianceRepository _profiles;
    private readonly ITokenReader _tokens;
    private readonly ITravelRuleReporter _travelRule;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public SyncHolderRegistryFromChainCommandHandler(
        IPropertyRepository properties,
        IHolderPositionRepository positions,
        IChainSyncCursorRepository cursors,
        IComplianceRepository profiles,
        ITokenReader tokens,
        ITravelRuleReporter travelRule,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _properties = properties;
        _positions = positions;
        _cursors = cursors;
        _profiles = profiles;
        _tokens = tokens;
        _travelRule = travelRule;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<int>> Handle(SyncHolderRegistryFromChainCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<int>(Error.NotFound("registry.propertyNotFound", "Property not found."));

        if (string.IsNullOrWhiteSpace(property.TokenContractAddress)
            || string.IsNullOrWhiteSpace(property.TokenChain))
        {
            return Result.Failure<int>(Error.Conflict(
                "registry.notDeployed", "This issue has no token contract to sync from."));
        }

        var now = _clock.UtcNow;
        var head = await _tokens.GetHeadBlockAsync(property.TokenChain, ct);
        var safeHead = head - ConfirmationDepth;
        if (safeHead <= 0)
            return Result.Success(0);

        var cursor = await _cursors.FindByPropertyAsync(property.Id, ct);
        if (cursor is null)
        {
            // First run replays from the start of the chain's history for this contract. Cheap here
            // because the contract is young; a deployment block on the issue would narrow it further.
            cursor = ChainSyncCursor.StartFor(property.Id, 0, now);
            await _cursors.AddAsync(cursor, ct);
        }

        var fromBlock = cursor.LastProcessedBlock == 0 ? 0 : cursor.LastProcessedBlock + 1;
        var toBlock = Math.Min(safeHead, fromBlock + MaxBlocksPerRun - 1);
        if (toBlock < fromBlock)
            return Result.Success(0);

        var transfers = await _tokens.GetTransfersAsync(
            property.TokenChain, property.TokenContractAddress, fromBlock, toBlock, ct);

        foreach (var transfer in transfers)
        {
            // A mint comes from the zero address and a burn goes to it; neither is a real holder, so
            // only the other side of those moves is applied.
            if (!IsZero(transfer.From))
                await AdjustAsync(property.Id, transfer.From, -transfer.Amount, now, ct);

            if (!IsZero(transfer.To))
                await AdjustAsync(property.Id, transfer.To, transfer.Amount, now, ct);

            // A move between two real holders is a transfer between service providers as far as the
            // travel rule is concerned, and this is the only place one is ever seen: a trade settles
            // on chain without telling the backend. Mints and burns are the platform issuing or
            // withdrawing shares, not a transfer between people, so they are skipped.
            if (!IsZero(transfer.From) && !IsZero(transfer.To))
            {
                await _travelRule.ReportTransferAsync(
                    property, transfer.From, transfer.To, transfer.Amount, transfer.TransactionHash, ct);
            }
        }

        cursor.AdvanceTo(toBlock, now);
        _cursors.Update(cursor);

        await _uow.SaveChangesAsync(ct);
        return Result.Success(transfers.Count);
    }

    private async Task AdjustAsync(
        Guid propertyId, string address, decimal delta, DateTime now, CancellationToken ct)
    {
        var position = await _positions.GetByAddressAsync(propertyId, address, ct);

        if (position is null)
        {
            if (delta <= 0)
                return;

            // An address the platform has never seen can legitimately appear: shares were traded to
            // someone we did not allowlist ourselves. It belongs in the register, linked to an
            // investor when one can be found and flagged when one cannot.
            var investorId = await ResolveInvestorAsync(address, ct);
            position = HolderPosition.Create(
                propertyId, address, delta, investorId, isAllowlisted: false, HolderSource.Chain, now);

            await _positions.AddAsync(position, ct);
            return;
        }

        position.Sync(Math.Max(position.TokenCount + delta, 0), HolderSource.Chain, now);
        _positions.Update(position);
    }

    private async Task<Guid?> ResolveInvestorAsync(string address, CancellationToken ct)
        => (await _profiles.GetByWalletAsync(address, ct))?.InvestorId;

    private static bool IsZero(string address)
        => string.Equals(address, ZeroAddress, StringComparison.OrdinalIgnoreCase);
}
