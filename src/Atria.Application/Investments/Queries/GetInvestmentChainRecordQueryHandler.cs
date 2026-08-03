using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;
using Atria.Domain.Users;

namespace Atria.Application.Investments.Queries;

/// <summary>
/// Assembles the on-chain coordinates of one investment so the holder can verify their shares
/// against the public record instead of against our screen.
/// </summary>
/// <remarks>
/// <para>
/// The addresses come off the investment rather than off the issue: the contract an allocation went
/// into is the contract that was current when it was minted, and reading it from the issue would
/// misdescribe old allocations the day an issue is ever redeployed. Only the network tag, which the
/// investment does not carry, is read from the issue.
/// </para>
/// <para>
/// Nothing here is invented when it is missing. An investment that has not been minted has no
/// transaction and says so; a network that has no explorer configured yields addresses without links.
/// A plausible-looking hash for an allocation that never happened is worse than an empty field.
/// </para>
/// </remarks>
public sealed class GetInvestmentChainRecordQueryHandler
    : IRequestHandler<GetInvestmentChainRecordQuery, Result<InvestmentChainRecordDto>>
{
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;
    private readonly IChainNetworkResolver _networks;
    private readonly ICurrentUserService _currentUser;

    public GetInvestmentChainRecordQueryHandler(
        IInvestmentRepository investments,
        IPropertyRepository properties,
        IChainNetworkResolver networks,
        ICurrentUserService currentUser)
    {
        _investments = investments;
        _properties = properties;
        _networks = networks;
        _currentUser = currentUser;
    }

    public async Task<Result<InvestmentChainRecordDto>> Handle(
        GetInvestmentChainRecordQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<InvestmentChainRecordDto>(
                Error.Unauthorized("investment.unauthorized", "Authentication is required."));

        var investment = await _investments.GetByIdAsync(request.InvestmentId, ct);

        // Someone else's investment answers not-found, as everywhere else: an address and a
        // transaction hash together identify a person's holding, and confirming that an id exists
        // would be enough to start looking for them on chain.
        var isOwner = investment is not null && investment.InvestorId == userId.Value;
        if (investment is null || (!isOwner && !_currentUser.IsInRole(Role.Admin)))
            return Result.Failure<InvestmentChainRecordDto>(
                Error.NotFound("investment.notFound", "Investment not found."));

        var property = await _properties.GetByIdAsync(investment.PropertyId, ct);
        var network = _networks.Resolve(property?.TokenChain);

        return Result.Success(new InvestmentChainRecordDto(
            investment.Id,
            investment.PropertyId,
            investment.TokenCount,
            investment.OnChainStatus,
            investment.WalletAddress,
            investment.TokenContractAddress,
            investment.TransactionHash,
            property?.TokenChain,
            network?.ChainId,
            _networks.ConfirmationsRequired,
            ExplorerUrl(network, "tx", investment.TransactionHash),
            ExplorerUrl(network, "address", investment.WalletAddress),
            ExplorerUrl(network, "address", investment.TokenContractAddress)));
    }

    /// <summary>
    /// A link only when there is both something to link to and an explorer to link into. Both parts
    /// matter: a half-built URL is a broken page for the holder, not a partially useful one.
    /// </summary>
    private static string? ExplorerUrl(ChainNetwork? network, string path, string? value)
        => string.IsNullOrWhiteSpace(network?.ExplorerBaseUrl) || string.IsNullOrWhiteSpace(value)
            ? null
            : $"{network.ExplorerBaseUrl.TrimEnd('/')}/{path}/{value}";
}
