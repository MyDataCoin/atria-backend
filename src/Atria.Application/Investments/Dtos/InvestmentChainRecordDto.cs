using Atria.Domain.Investments;

namespace Atria.Application.Investments.Dtos;

/// <summary>
/// What the holder can check for themselves about their shares on chain (draft Decree, §21): the
/// address the shares were issued to, the contract they live in, the transaction that issued them and
/// whether it has settled.
/// </summary>
/// <remarks>
/// <para>
/// The point of showing this is that the holder does not have to take our word for it. Everything
/// here is a coordinate into public data: with the contract address and their own address anyone can
/// read the balance from the chain, and with the transaction hash they can read the issuance itself.
/// The explorer links are a convenience over that, not the source of truth.
/// </para>
/// <para>
/// The links are built here rather than in the dashboard because which explorer serves a network is
/// a fact about the network, and it is configured next to the RPC endpoint. A frontend that composes
/// its own URLs quietly keeps pointing at the testnet explorer after the issue moves to mainnet.
/// </para>
/// </remarks>
/// <param name="InvestmentId">The investment this record belongs to.</param>
/// <param name="PropertyId">The issue whose shares these are.</param>
/// <param name="TokenCount">How many whole shares the allocation covers.</param>
/// <param name="Status">Whether the allocation is pending, settled, failed, or not on chain at all.</param>
/// <param name="WalletAddress">The holder's address the shares were issued to.</param>
/// <param name="TokenContractAddress">The issue's own token contract.</param>
/// <param name="TransactionHash">The issuing transaction, once one has been submitted.</param>
/// <param name="ChainTag">The network tag recorded on the issue, e.g. <c>bsc-testnet</c>.</param>
/// <param name="ChainId">EIP-155 chain id, so the holder can tell which network this is.</param>
/// <param name="ConfirmationsRequired">Blocks we wait for before calling the allocation settled.</param>
/// <param name="TransactionUrl">Explorer page for the transaction, when an explorer is configured.</param>
/// <param name="WalletUrl">Explorer page for the holder's address.</param>
/// <param name="ContractUrl">Explorer page for the token contract.</param>
public sealed record InvestmentChainRecordDto(
    Guid InvestmentId,
    Guid PropertyId,
    decimal TokenCount,
    OnChainStatus Status,
    string? WalletAddress,
    string? TokenContractAddress,
    string? TransactionHash,
    string? ChainTag,
    long? ChainId,
    int ConfirmationsRequired,
    string? TransactionUrl,
    string? WalletUrl,
    string? ContractUrl);
