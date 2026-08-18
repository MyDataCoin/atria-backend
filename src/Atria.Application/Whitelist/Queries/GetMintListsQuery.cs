using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Whitelist.Dtos;

namespace Atria.Application.Whitelist.Queries;

/// <summary>Mint-list headers, newest first. Operator only.</summary>
/// <param name="PropertyId">Narrow to one issuance; <c>null</c> returns batches across all of them.</param>
public sealed record GetMintListsQuery(Guid? PropertyId) : IRequest<Result<IReadOnlyList<MintListDto>>>;

public sealed class GetMintListsQueryHandler
    : IRequestHandler<GetMintListsQuery, Result<IReadOnlyList<MintListDto>>>
{
    private readonly IMintListRepository _lists;

    public GetMintListsQueryHandler(IMintListRepository lists) => _lists = lists;

    public async Task<Result<IReadOnlyList<MintListDto>>> Handle(GetMintListsQuery request, CancellationToken ct)
    {
        var rows = await _lists.ListAsync(request.PropertyId, ct);

        IReadOnlyList<MintListDto> dtos = rows.Select(r => MintListMapper.ToDto(r.List, r.PropertyName)).ToList();

        return Result.Success(dtos);
    }
}

/// <summary>Shapes a batch into its DTO. One place, so the list and the detail read never drift apart.</summary>
internal static class MintListMapper
{
    public static MintListDto ToDto(Domain.Whitelist.MintList list, string? propertyName)
        => new(
            list.Id, list.Number, list.PropertyId, propertyName, list.TokenContractAddress, list.TokenChain,
            list.Status, list.ItemCount, list.TotalTokens, list.CreatedByUserId, list.CreatedAtUtc,
            list.SentAtUtc, list.ExecutedAtUtc, list.Note, list.CancellationReason);
}
