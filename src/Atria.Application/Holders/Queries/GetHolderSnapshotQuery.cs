using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Holders.Dtos;

namespace Atria.Application.Holders.Queries;

/// <summary>A frozen snapshot with all of its rows. Operator only.</summary>
/// <param name="Id">Snapshot identifier.</param>
public sealed record GetHolderSnapshotQuery(Guid Id) : IRequest<Result<HolderSnapshotDetailDto>>;

public sealed class GetHolderSnapshotQueryHandler
    : IRequestHandler<GetHolderSnapshotQuery, Result<HolderSnapshotDetailDto>>
{
    private readonly IHolderSnapshotRepository _snapshots;

    public GetHolderSnapshotQueryHandler(IHolderSnapshotRepository snapshots) => _snapshots = snapshots;

    public async Task<Result<HolderSnapshotDetailDto>> Handle(GetHolderSnapshotQuery request, CancellationToken ct)
    {
        var snapshot = await _snapshots.GetWithRowsAsync(request.Id, ct);
        if (snapshot is null)
            return Result.Failure<HolderSnapshotDetailDto>(
                Error.NotFound("holderSnapshot.notFound", "Snapshot not found."));

        // Rows are frozen in address order; keep that order so an export is byte-for-byte reproducible.
        var rows = snapshot.Rows
            .OrderBy(r => r.WalletAddress, StringComparer.Ordinal)
            .Select(HolderSnapshotMapper.ToDto)
            .ToList();

        return Result.Success(new HolderSnapshotDetailDto(HolderSnapshotMapper.ToDto(snapshot), rows));
    }
}
