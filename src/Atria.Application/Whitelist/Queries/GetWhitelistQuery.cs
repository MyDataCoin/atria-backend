using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Whitelist.Dtos;
using Atria.Domain.Investments;
using Atria.Domain.Whitelist;

namespace Atria.Application.Whitelist.Queries;

/// <summary>
/// The whitelist queue — every purchase request and where it stands on the way to being minted.
/// Operator only: it crosses investor boundaries by design.
/// </summary>
/// <param name="PropertyId">Narrow to one issuance; <c>null</c> returns the queue across all of them.</param>
/// <param name="Status">Narrow to one status; <c>null</c> returns every status.</param>
/// <param name="Take">How many rows to return, capped at 500.</param>
public sealed record GetWhitelistQuery(Guid? PropertyId, WhitelistStatus? Status, int Take = 200)
    : IRequest<Result<IReadOnlyList<WhitelistEntryDto>>>;

public sealed class GetWhitelistQueryHandler
    : IRequestHandler<GetWhitelistQuery, Result<IReadOnlyList<WhitelistEntryDto>>>
{
    private readonly IWhitelistEntryRepository _entries;
    private readonly IInvestmentRepository _investments;

    public GetWhitelistQueryHandler(IWhitelistEntryRepository entries, IInvestmentRepository investments)
    {
        _entries = entries;
        _investments = investments;
    }

    public async Task<Result<IReadOnlyList<WhitelistEntryDto>>> Handle(
        GetWhitelistQuery request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 500);
        var rows = await _entries.ListForOperatorAsync(request.PropertyId, request.Status, take, ct);

        // Entries whose investment the platform already mints by itself after approval. Shown as such
        // so the operator does not try to batch them (the mint-list command refuses that anyway).
        var investmentIds = rows.Select(r => r.Entry.InvestmentId).Distinct().ToList();
        var inFlight = investmentIds.Count == 0
            ? new HashSet<Guid>()
            : (await _investments.GetByIdsAsync(investmentIds, ct))
                .Where(i => i.OnChainStatus != OnChainStatus.None)
                .Select(i => i.Id)
                .ToHashSet();

        IReadOnlyList<WhitelistEntryDto> dtos = rows
            .Select(r => new WhitelistEntryDto(
                r.Entry.Id, r.Entry.InvestmentId, r.Entry.InvestorId, r.Entry.PropertyId, r.PropertyName,
                r.Entry.TokenCount, r.Entry.WalletAddress, r.Entry.Status, r.Entry.RequestedAtUtc,
                r.Entry.ApprovedAtUtc, r.Entry.MintListId, r.MintListNumber, r.Entry.MintedAtUtc,
                r.Entry.ExclusionReason, inFlight.Contains(r.Entry.InvestmentId)))
            .ToList();

        return Result.Success(dtos);
    }
}
