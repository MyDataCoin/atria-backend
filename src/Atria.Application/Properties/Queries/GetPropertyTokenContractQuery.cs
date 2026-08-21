using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Queries;

/// <summary>
/// The on-chain binding of an issue: which contract carries its shares, on which network, and where
/// the issuer holds its own position. Kept out of <c>PropertyDto</c> — the catalogue is public and
/// this is operational detail, readable only once the deployment is something an admin should see.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="TokenContractAddress">Address of the token contract, or null while the issue is not deployed.</param>
/// <param name="TokenChain">Tag of the network it lives on, or null.</param>
/// <param name="ChainId">EIP-155 chain id of that network, or null when the tag is no longer configured.</param>
/// <param name="IssuerWalletAddress">Wallet the issuer holds the issue's own shares in, or null.</param>
/// <param name="ExplorerUrl">Public explorer link to the contract, when the network has one configured.</param>
public sealed record PropertyTokenContractDto(
    Guid PropertyId,
    string? TokenContractAddress,
    string? TokenChain,
    long? ChainId,
    string? IssuerWalletAddress,
    string? ExplorerUrl);

/// <summary>Reads the on-chain binding of an issue.</summary>
/// <param name="PropertyId">The issue.</param>
public sealed record GetPropertyTokenContractQuery(Guid PropertyId) : IRequest<Result<PropertyTokenContractDto>>;

public sealed class GetPropertyTokenContractQueryHandler
    : IRequestHandler<GetPropertyTokenContractQuery, Result<PropertyTokenContractDto>>
{
    private readonly IPropertyRepository _properties;
    private readonly IChainNetworkResolver _networks;

    public GetPropertyTokenContractQueryHandler(
        IPropertyRepository properties, IChainNetworkResolver networks)
    {
        _properties = properties;
        _networks = networks;
    }

    public async Task<Result<PropertyTokenContractDto>> Handle(
        GetPropertyTokenContractQuery request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<PropertyTokenContractDto>(
                Error.NotFound("property.notFound", "Property not found."));

        var network = _networks.Resolve(property.TokenChain);
        var explorer = string.IsNullOrWhiteSpace(network?.ExplorerBaseUrl)
            || string.IsNullOrWhiteSpace(property.TokenContractAddress)
                ? null
                : $"{network.ExplorerBaseUrl.TrimEnd('/')}/address/{property.TokenContractAddress}";

        return Result.Success(new PropertyTokenContractDto(
            property.Id, property.TokenContractAddress, property.TokenChain,
            network?.ChainId, property.IssuerWalletAddress, explorer));
    }
}
