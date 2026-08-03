using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Holders.Queries;
using Atria.Domain.Holders;

namespace Atria.Application.Holders.Commands;

/// <summary>Freezes an issuance's holder register at a cut. Operator only.</summary>
/// <param name="PropertyId">The issuance to snapshot.</param>
/// <param name="SnapshotAtUtc">The cut to freeze; <c>null</c> means now. Must not be in the future.</param>
/// <param name="Purpose">Why the snapshot is taken — a payout run and a regulatory statement on the same
/// date are separate, independently auditable snapshots.</param>
public sealed record CreateHolderSnapshotCommand(
    Guid PropertyId, DateTime? SnapshotAtUtc, SnapshotPurpose Purpose) : IRequest<Result<Guid>>;

/// <summary>
/// Builds an immutable snapshot from the current holder positions of an issuance.
///
/// Idempotent by (property, cut, purpose): asking twice for the same cut returns the snapshot already
/// taken instead of building a second one from since-changed holdings. That is what makes a snapshot
/// stable — trades settled after it was taken never alter it, and a payout recomputed against the same
/// cut gets the same register back. Recomputing a past payout differently means taking a new snapshot,
/// never editing the old one.
///
/// Once the issue is on chain, the registry is reconciled against the contract first and a
/// discrepancy blocks the snapshot outright. A payout or a regulatory statement drawn on a register
/// that disagrees with the chain is worse than a missing one: the numbers look authoritative and are
/// wrong, and the money moves on them.
/// </summary>
public sealed class CreateHolderSnapshotCommandHandler
    : IRequestHandler<CreateHolderSnapshotCommand, Result<Guid>>
{
    private readonly IHolderSnapshotRepository _snapshots;
    private readonly IHolderPositionRepository _positions;
    private readonly IPropertyRepository _properties;
    private readonly ISender _sender;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public CreateHolderSnapshotCommandHandler(
        IHolderSnapshotRepository snapshots,
        IHolderPositionRepository positions,
        IPropertyRepository properties,
        ISender sender,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _snapshots = snapshots;
        _positions = positions;
        _properties = properties;
        _sender = sender;
        _currentUser = currentUser;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<Guid>> Handle(CreateHolderSnapshotCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<Guid>(
                Error.Unauthorized("holderSnapshot.unauthorized", "Authentication is required."));

        var now = _clock.UtcNow;
        var cut = request.SnapshotAtUtc ?? now;
        if (cut > now)
            return Result.Failure<Guid>(Error.Validation(
                "holderSnapshot.futureCut", "A snapshot cannot be taken for a future instant."));

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<Guid>(Error.NotFound("holderSnapshot.propertyNotFound", "Property not found."));

        var existing = await _snapshots.FindByCutAsync(request.PropertyId, cut, request.Purpose, ct);
        if (existing is not null)
            return Result.Success(existing.Id);

        // Deployed issues are checked against the chain before anything is frozen. An issue with no
        // contract yet has nothing to disagree with, so it skips straight to the cut.
        if (!string.IsNullOrWhiteSpace(property.TokenContractAddress))
        {
            var reconciliation = await _sender.Send(new ReconcileRegistryQuery(property.Id), ct);
            if (reconciliation.IsFailure)
                return Result.Failure<Guid>(reconciliation.Error);

            if (!reconciliation.Value.IsClean)
            {
                var detail = string.Join(" ", reconciliation.Value.Discrepancies.Select(d => d.Detail));
                return Result.Failure<Guid>(Error.Conflict(
                    "holderSnapshot.registryDiscrepancy",
                    $"Реестр расходится с состоянием сети, срез не снят. {detail}"));
            }
        }

        var positions = await _positions.GetByPropertyAsync(request.PropertyId, ct);
        var entries = positions.Select(p => new HolderSnapshotEntry(p.WalletAddress, p.TokenCount, p.InvestorId));

        // BlockNumber stays null while the registry is projected from our own records; it is filled once
        // chain reading is wired and the cut can be pinned to a block height.
        var snapshot = HolderSnapshot.Create(
            request.PropertyId, cut, request.Purpose, blockNumber: null, userId.Value, entries);

        await _snapshots.AddAsync(snapshot, ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success(snapshot.Id);
    }
}
