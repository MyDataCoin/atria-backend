using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Holders.Dtos;
using Atria.Domain.Holders;

namespace Atria.Application.Holders.Queries;

/// <summary>Snapshot headers taken for an issuance, newest cut first. Operator only.</summary>
/// <param name="PropertyId">The issuance whose snapshots to list.</param>
public sealed record GetHolderSnapshotsQuery(Guid PropertyId)
    : IRequest<Result<IReadOnlyList<HolderSnapshotDto>>>;

public sealed class GetHolderSnapshotsQueryHandler
    : IRequestHandler<GetHolderSnapshotsQuery, Result<IReadOnlyList<HolderSnapshotDto>>>
{
    private readonly IHolderSnapshotRepository _snapshots;

    public GetHolderSnapshotsQueryHandler(IHolderSnapshotRepository snapshots) => _snapshots = snapshots;

    public async Task<Result<IReadOnlyList<HolderSnapshotDto>>> Handle(
        GetHolderSnapshotsQuery request, CancellationToken ct)
    {
        var snapshots = await _snapshots.ListByPropertyAsync(request.PropertyId, ct);
        IReadOnlyList<HolderSnapshotDto> dtos = snapshots.Select(HolderSnapshotMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Shared projection so the list, the detail view and the export all describe a snapshot alike.</summary>
internal static class HolderSnapshotMapper
{
    public static HolderSnapshotDto ToDto(HolderSnapshot s) => new(
        s.Id, s.PropertyId, s.SnapshotAtUtc, s.Purpose, s.BlockNumber,
        s.TotalTokens, s.AddressCount, s.CreatedByUserId, s.CreatedAtUtc);

    public static HolderSnapshotRowDto ToDto(HolderSnapshotRow r) =>
        new(r.WalletAddress, r.TokenCount, r.InvestorId, r.Share);
}
