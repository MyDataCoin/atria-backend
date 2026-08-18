using System.Globalization;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Whitelist.Dtos;

namespace Atria.Application.Whitelist.Queries;

/// <summary>A mint list rendered as CSV — the file handed to the exchange. Operator only.</summary>
/// <param name="Id">Batch identifier.</param>
public sealed record ExportMintListQuery(Guid Id) : IRequest<Result<MintListFileDto>>;

/// <summary>
/// Renders a batch as CSV. Generated on the server, never in the browser, so what the exchange receives
/// is exactly what the batch holds.
///
/// Deterministic by construction: lines keep the address order they were frozen in, numbers are written
/// with an invariant format, and nothing about the moment of export enters the file — exporting the
/// same batch twice yields identical bytes, which is what lets both sides compare what they hold.
///
/// The contract address and chain ride along on every line rather than in a header, because the file
/// has to survive being opened in a spreadsheet and pasted somewhere else: a header would be dropped in
/// the paste and the rows would no longer say what they mint on.
/// </summary>
public sealed class ExportMintListQueryHandler : IRequestHandler<ExportMintListQuery, Result<MintListFileDto>>
{
    private readonly IMintListRepository _lists;

    public ExportMintListQueryHandler(IMintListRepository lists) => _lists = lists;

    public async Task<Result<MintListFileDto>> Handle(ExportMintListQuery request, CancellationToken ct)
    {
        var list = await _lists.GetWithItemsAsync(request.Id, ct);
        if (list is null)
            return Result.Failure<MintListFileDto>(Error.NotFound("mintList.notFound", "Mint list not found."));

        var csv = new StringBuilder();
        csv.Append("mint_list,chain,token_contract,wallet_address,token_count,investment_id,investor_id\n");

        foreach (var item in list.Items.OrderBy(i => i.WalletAddress, StringComparer.Ordinal))
        {
            csv.Append(Csv(list.Number)).Append(',')
                .Append(Csv(list.TokenChain ?? string.Empty)).Append(',')
                .Append(Csv(list.TokenContractAddress ?? string.Empty)).Append(',')
                .Append(Csv(item.WalletAddress)).Append(',')
                .Append(item.TokenCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.InvestmentId).Append(',')
                .Append(item.InvestorId).Append('\n');
        }

        var fileName = $"mint-list-{list.Number}.csv";

        // UTF-8 with a BOM: Excel otherwise reads the file as the local ANSI code page.
        var content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());

        return Result.Success(new MintListFileDto(fileName, "text/csv; charset=utf-8", content));
    }

    /// <summary>Quotes a field when it carries a separator, a quote or a newline; doubles inner quotes.</summary>
    private static string Csv(string value)
        => value.AsSpan().IndexOfAny(",\"\n\r") >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
