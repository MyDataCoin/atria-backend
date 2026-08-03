using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Holders.Queries;

/// <summary>One thing that does not add up between the registry and the chain.</summary>
/// <param name="Code">Stable identifier of the check that failed.</param>
/// <param name="Detail">What was expected and what was found, in words an operator can act on.</param>
public sealed record RegistryDiscrepancy(string Code, string Detail);

/// <summary>The outcome of comparing an issue's registry against its token contract.</summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="CheckedAtUtc">When the comparison ran.</param>
/// <param name="RegistryTotal">Shares the registry believes are held.</param>
/// <param name="ChainTotalSupply">Shares the contract says exist.</param>
/// <param name="SoldTokens">Shares the issue records as placed.</param>
/// <param name="Discrepancies">Everything that disagreed; empty means the registry is trustworthy.</param>
public sealed record RegistryReconciliationReport(
    Guid PropertyId,
    DateTime CheckedAtUtc,
    long RegistryTotal,
    long ChainTotalSupply,
    long SoldTokens,
    IReadOnlyList<RegistryDiscrepancy> Discrepancies)
{
    /// <summary>True when nothing disagreed.</summary>
    public bool IsClean => Discrepancies.Count == 0;
}

/// <summary>Compares an issue's holder registry against its token contract. Operator only.</summary>
/// <param name="PropertyId">The issue to check.</param>
public sealed record ReconcileRegistryQuery(Guid PropertyId)
    : IRequest<Result<RegistryReconciliationReport>>;

/// <summary>
/// Runs the three checks that decide whether the registry can be relied on:
/// the shares it counts equal the contract's total supply, they equal what the issue records as
/// placed, and every address holding shares is on the allowlist.
/// </summary>
/// <remarks>
/// The three catch different failures. Supply mismatch means shares exist that the registry never
/// saw, or the registry counts shares that were never minted. A mismatch against what was placed
/// means the platform's own books disagree with themselves. An unlisted holder means shares reached
/// an address compliance never approved — which is the one the regulator asks about.
/// </remarks>
public sealed class ReconcileRegistryQueryHandler
    : IRequestHandler<ReconcileRegistryQuery, Result<RegistryReconciliationReport>>
{
    /// <summary>Burn address: shares sent here are destroyed, not held by anyone.</summary>
    private const string ZeroAddress = "0x0000000000000000000000000000000000000000";

    private readonly IPropertyRepository _properties;
    private readonly IHolderPositionRepository _positions;
    private readonly ITokenReader _tokens;
    private readonly IDateTimeProvider _clock;

    public ReconcileRegistryQueryHandler(
        IPropertyRepository properties,
        IHolderPositionRepository positions,
        ITokenReader tokens,
        IDateTimeProvider clock)
    {
        _properties = properties;
        _positions = positions;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<Result<RegistryReconciliationReport>> Handle(
        ReconcileRegistryQuery request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<RegistryReconciliationReport>(
                Error.NotFound("registry.propertyNotFound", "Property not found."));

        if (string.IsNullOrWhiteSpace(property.TokenContractAddress)
            || string.IsNullOrWhiteSpace(property.TokenChain))
        {
            return Result.Failure<RegistryReconciliationReport>(Error.Conflict(
                "registry.notDeployed",
                "This issue has no token contract yet, so there is nothing to reconcile against."));
        }

        var positions = await _positions.GetByPropertyAsync(property.Id, ct);
        var holders = positions.Where(p => p.TokenCount > 0).ToList();

        var registryTotal = holders.Sum(p => p.TokenCount);
        var soldTokens = property.TotalTokens - property.AvailableTokens;

        long chainSupply;
        try
        {
            chainSupply = await _tokens.GetTotalSupplyAsync(
                property.TokenChain, property.TokenContractAddress, ct);
        }
        catch (Exception ex)
        {
            // An unreachable node is not a discrepancy. Saying "reconciled, all good" because the
            // chain could not be asked is exactly the failure this whole check exists to prevent.
            return Result.Failure<RegistryReconciliationReport>(Error.ExternalService(
                "registry.chainUnavailable", $"Could not read the token contract: {ex.Message}"));
        }

        var discrepancies = new List<RegistryDiscrepancy>();

        if (registryTotal != chainSupply)
        {
            discrepancies.Add(new RegistryDiscrepancy(
                "registry.supplyMismatch",
                $"Реестр насчитывает {registryTotal} долей, контракт — {chainSupply}."));
        }

        if (registryTotal != soldTokens)
        {
            discrepancies.Add(new RegistryDiscrepancy(
                "registry.soldMismatch",
                $"Реестр насчитывает {registryTotal} долей, по выпуску размещено {soldTokens}."));
        }

        foreach (var holder in holders.Where(h => !h.IsAllowlisted))
        {
            // Not an error to be swallowed: an address can lose its allowlist standing and keep its
            // shares, and the register has to say so out loud rather than pretend it is fine.
            discrepancies.Add(new RegistryDiscrepancy(
                "registry.holderNotAllowlisted",
                $"Адрес {holder.WalletAddress} держит {holder.TokenCount} долей, но не в белом списке."));
        }

        foreach (var holder in holders.Where(h => string.Equals(
                     h.WalletAddress, ZeroAddress, StringComparison.OrdinalIgnoreCase)))
        {
            discrepancies.Add(new RegistryDiscrepancy(
                "registry.zeroAddressHolder",
                $"Нулевой адрес числится держателем {holder.TokenCount} долей — это сожжённые доли."));
        }

        return Result.Success(new RegistryReconciliationReport(
            property.Id, _clock.UtcNow, registryTotal, chainSupply, soldTokens, discrepancies));
    }
}
