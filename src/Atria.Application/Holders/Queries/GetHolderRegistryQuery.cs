using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Holders.Dtos;

namespace Atria.Application.Holders.Queries;

/// <summary>The current holder register of an issuance, optionally filtered. Operator only.</summary>
/// <param name="PropertyId">The issuance whose register to read.</param>
/// <param name="Search">Address fragment or investor id; <c>null</c> returns every holder.</param>
public sealed record GetHolderRegistryQuery(Guid PropertyId, string? Search)
    : IRequest<Result<IReadOnlyList<HolderPositionDto>>>;

/// <summary>
/// Reads the live positions of an issuance, largest holding first. Zero-balance positions are kept:
/// an address that sold out is still part of the register's history and its row explains where the
/// shares went. Delisted addresses are kept too — we hold no keys and cannot claw shares back, so a
/// non-allowlisted holder with a balance is a real, flagged state rather than something to hide.
/// </summary>
public sealed class GetHolderRegistryQueryHandler
    : IRequestHandler<GetHolderRegistryQuery, Result<IReadOnlyList<HolderPositionDto>>>
{
    private readonly IHolderPositionRepository _positions;
    private readonly IPropertyRepository _properties;

    public GetHolderRegistryQueryHandler(IHolderPositionRepository positions, IPropertyRepository properties)
    {
        _positions = positions;
        _properties = properties;
    }

    public async Task<Result<IReadOnlyList<HolderPositionDto>>> Handle(
        GetHolderRegistryQuery request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<IReadOnlyList<HolderPositionDto>>(
                Error.NotFound("holderRegistry.propertyNotFound", "Property not found."));

        var positions = await _positions.GetByPropertyAsync(request.PropertyId, ct);

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Two ways an operator looks a holder up: by the address itself, or by the investor behind
            // it. An investor id only ever matches whole, an address matches on any fragment.
            var byInvestor = Guid.TryParse(search, out var investorId);
            positions = positions
                .Where(p =>
                    p.WalletAddress.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || (byInvestor && p.InvestorId == investorId))
                .ToList();
        }

        IReadOnlyList<HolderPositionDto> dtos = positions
            .OrderByDescending(p => p.TokenCount)
            .ThenBy(p => p.WalletAddress, StringComparer.Ordinal)
            .Select(p => new HolderPositionDto(
                p.WalletAddress, p.TokenCount, p.InvestorId, p.IsAllowlisted, p.Source, p.LastSyncedAtUtc))
            .ToList();

        return Result.Success(dtos);
    }
}
