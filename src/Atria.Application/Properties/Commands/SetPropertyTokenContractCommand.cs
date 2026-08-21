using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Binds a deployed token contract to an issue: the address of the contract, the network it lives on
/// and the wallet that holds the issuer's own position.
/// </summary>
/// <remarks>
/// Until this is recorded, every chain-facing feature of the issue is inert — mint batches carry no
/// contract, the holder register has nothing to sync against and collateral is never attested. The
/// binding used to be settable only by editing the database by hand, which is exactly the kind of
/// change that should leave a journal entry behind.
/// </remarks>
/// <param name="PropertyId">The issue.</param>
/// <param name="TokenContractAddress">Address of the deployed permissioned token contract.</param>
/// <param name="TokenChain">Tag of the network it is deployed on, e.g. <c>bsc-testnet</c>.</param>
/// <param name="IssuerWalletAddress">Wallet the issuer holds the issue's own shares in.</param>
public sealed record SetPropertyTokenContractCommand(
    Guid PropertyId,
    string TokenContractAddress,
    string TokenChain,
    string IssuerWalletAddress) : IRequest<Result>;

public sealed class SetPropertyTokenContractCommandHandler
    : IRequestHandler<SetPropertyTokenContractCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IChainNetworkResolver _networks;
    private readonly IHolderPositionRepository _holders;
    private readonly IMintListRepository _mintLists;
    private readonly ITokenReader _tokens;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public SetPropertyTokenContractCommandHandler(
        IPropertyRepository properties,
        IChainNetworkResolver networks,
        IHolderPositionRepository holders,
        IMintListRepository mintLists,
        ITokenReader tokens,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _properties = properties;
        _networks = networks;
        _holders = holders;
        _mintLists = mintLists;
        _tokens = tokens;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(SetPropertyTokenContractCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        if (!WalletAddress.IsValid(request.TokenContractAddress))
            return Result.Failure(Error.Validation(
                "property.invalidTokenContract",
                "Token contract address must be an EVM address (0x + 40 hex digits)."));

        if (!WalletAddress.IsValid(request.IssuerWalletAddress))
            return Result.Failure(Error.Validation(
                "property.invalidIssuerWallet",
                "Issuer wallet address must be an EVM address (0x + 40 hex digits)."));

        // The tag is not free text: it is the key every chain operation later resolves an RPC endpoint,
        // a chain id and the shared registry addresses from. A typo here would only surface as a
        // mint that never leaves the queue.
        var network = _networks.Resolve(request.TokenChain);
        if (network is null)
            return Result.Failure(Error.Validation(
                "property.unknownChain",
                $"Network '{request.TokenChain}' is not configured. Configured: "
                + $"{string.Join(", ", _networks.All.Select(n => n.Tag))}."));

        // Stored lowercase because everything that later compares an address to this one — the holder
        // register, the mint batches, the reconciliation report — compares ordinally.
        var contract = request.TokenContractAddress.ToLowerInvariant();
        var issuer = request.IssuerWalletAddress.ToLowerInvariant();

        var alreadyBound = !string.IsNullOrWhiteSpace(property.TokenContractAddress);
        var movesContract = alreadyBound
            && (!string.Equals(property.TokenContractAddress, contract, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(property.TokenChain, network.Tag, StringComparison.OrdinalIgnoreCase));

        // Re-pointing an issue at a different contract after shares exist would silently detach the
        // register from the shares it describes: the holders stay in the database, the balances they
        // came from are on a contract nobody reads any more. Once anything has been issued the binding
        // is final, and a mistake is corrected by re-issuing rather than by editing the pointer.
        if (movesContract && await HasBeenIssuedAsync(property.Id, ct))
            return Result.Failure(Error.Conflict(
                "property.tokenContractAlreadyBound",
                "This issue is already bound to a token contract that has been issued against. "
                + "The binding cannot be moved."));

        // The contract names the issue it was deployed for. Asking it, rather than trusting the
        // address typed into this request, is the difference between a register that can be checked
        // and one that is merely asserted: a right address and a wrong one look identical here, and
        // the mistake only surfaces once the register has been reconciling against someone else's
        // shares for a while.
        var expected = PropertyChainId.From(property.Id);
        var onChain = await _tokens.GetPropertyIdAsync(network.Tag, contract, ct);

        if (onChain is not null && !PropertyChainId.Matches(property.Id, onChain))
            return Result.Failure(Error.Conflict(
                "property.tokenContractIdMismatch",
                string.Equals(onChain, PropertyChainId.Unset, StringComparison.OrdinalIgnoreCase)
                    ? $"This contract was deployed with an empty propertyId. Deploy it with "
                      + $"PROPERTY_ID={expected} — the id cannot be set afterwards."
                    : $"This contract belongs to another issue: it declares propertyId {onChain}, "
                      + $"and this issue is {expected}."));

        try
        {
            property.SetTokenContract(contract, network.Tag, issuer);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Validation("property.invalidTokenContract", ex.Message));
        }

        _properties.Update(property);

        // Recorded as a warning when the contract could not be asked: the binding is in force either
        // way, and which of the two it was is exactly what someone reconciling a suspect register
        // needs to know later.
        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PropertyUpdated,
            onChain is null
                ? $"Объекту «{property.Name}» назначен токен-контракт {contract} в сети {network.Tag}; "
                  + "сверить propertyId с контрактом не удалось"
                : $"Объекту «{property.Name}» назначен токен-контракт {contract} в сети {network.Tag}, "
                  + "propertyId сверен",
            onChain is null ? AuditSeverity.Warning : AuditSeverity.Success, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Whether anything has been issued against the current binding: a holder position synced from
    /// chain, or a mint batch handed to the exchange. Either one means addresses out there refer to
    /// the contract this issue currently points at.
    /// </summary>
    private async Task<bool> HasBeenIssuedAsync(Guid propertyId, CancellationToken ct)
    {
        var positions = await _holders.GetByPropertyAsync(propertyId, ct);
        if (positions.Count > 0)
            return true;

        var batches = await _mintLists.ListAsync(propertyId, ct);
        return batches.Count > 0;
    }
}
