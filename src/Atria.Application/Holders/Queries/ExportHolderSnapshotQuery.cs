using System.Globalization;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Holders.Dtos;

namespace Atria.Application.Holders.Queries;

/// <summary>A frozen snapshot rendered as CSV for download. Operator only.</summary>
/// <param name="Id">Snapshot identifier.</param>
public sealed record ExportHolderSnapshotQuery(Guid Id) : IRequest<Result<HolderSnapshotFileDto>>;

/// <summary>
/// Renders a snapshot as CSV. The file is generated on the server, never in the browser, so what the
/// operator hands to a regulator is exactly what the register holds.
///
/// Deterministic by construction: rows keep the address order they were frozen in, numbers are written
/// with an invariant format, and no timestamp of the export itself enters the file — exporting the same
/// snapshot twice yields identical bytes.
/// </summary>
public sealed class ExportHolderSnapshotQueryHandler
    : IRequestHandler<ExportHolderSnapshotQuery, Result<HolderSnapshotFileDto>>
{
    private readonly IHolderSnapshotRepository _snapshots;

    public ExportHolderSnapshotQueryHandler(IHolderSnapshotRepository snapshots) => _snapshots = snapshots;

    public async Task<Result<HolderSnapshotFileDto>> Handle(ExportHolderSnapshotQuery request, CancellationToken ct)
    {
        var snapshot = await _snapshots.GetWithRowsAsync(request.Id, ct);
        if (snapshot is null)
            return Result.Failure<HolderSnapshotFileDto>(
                Error.NotFound("holderSnapshot.notFound", "Snapshot not found."));

        var csv = new StringBuilder();
        csv.Append("wallet_address,token_count,share,investor_id\n");

        foreach (var row in snapshot.Rows.OrderBy(r => r.WalletAddress, StringComparer.Ordinal))
        {
            csv.Append(Csv(row.WalletAddress)).Append(',')
                .Append(row.TokenCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Share.ToString("F8", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.InvestorId?.ToString() ?? string.Empty).Append('\n');
        }

        var fileName = $"holder-snapshot-{snapshot.PropertyId}-{snapshot.SnapshotAtUtc:yyyyMMdd'T'HHmmss'Z'}.csv";

        // UTF-8 with a BOM: Excel otherwise reads the file as the local ANSI code page.
        var content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());

        return Result.Success(new HolderSnapshotFileDto(fileName, "text/csv; charset=utf-8", content));
    }

    /// <summary>Quotes a field when it carries a separator, a quote or a newline; doubles inner quotes.</summary>
    private static string Csv(string value)
        => value.AsSpan().IndexOfAny(",\"\n\r") >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
