using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Whitelist.Dtos;

namespace Atria.Application.Whitelist.Queries;

/// <summary>
/// A batch with all of its lines — the JSON form of what the exchange is handed, for an exchange that
/// would rather pull the batch than be sent a file. Operator only.
/// </summary>
/// <param name="Id">Batch identifier.</param>
public sealed record GetMintListQuery(Guid Id) : IRequest<Result<MintListDetailDto>>;

public sealed class GetMintListQueryHandler : IRequestHandler<GetMintListQuery, Result<MintListDetailDto>>
{
    private readonly IMintListRepository _lists;
    private readonly IPropertyRepository _properties;

    public GetMintListQueryHandler(IMintListRepository lists, IPropertyRepository properties)
    {
        _lists = lists;
        _properties = properties;
    }

    public async Task<Result<MintListDetailDto>> Handle(GetMintListQuery request, CancellationToken ct)
    {
        var list = await _lists.GetWithItemsAsync(request.Id, ct);
        if (list is null)
            return Result.Failure<MintListDetailDto>(Error.NotFound("mintList.notFound", "Mint list not found."));

        var property = await _properties.GetByIdAsync(list.PropertyId, ct);

        // Ordered by address, the order the lines were frozen in: the same batch always reads the same.
        IReadOnlyList<MintListItemDto> items = list.Items
            .OrderBy(i => i.WalletAddress, StringComparer.Ordinal)
            .Select(i => new MintListItemDto(
                i.WhitelistEntryId, i.InvestmentId, i.InvestorId, i.WalletAddress, i.TokenCount))
            .ToList();

        return Result.Success(new MintListDetailDto(MintListMapper.ToDto(list, property?.Name), items));
    }
}
