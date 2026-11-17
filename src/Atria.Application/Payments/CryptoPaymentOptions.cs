using Atria.Domain.Payments;

namespace Atria.Application.Payments;

/// <summary>Chain-side configuration of crypto payments (section <c>CryptoPayments</c>).</summary>
/// <remarks>
/// Wallets and the rate are NOT here — the operator sets those in the admin panel
/// (<see cref="PaymentSettings"/>). This is what does not change between operators: the canonical
/// USDT contracts and how the chains are reached. Payments are always read on MAINNET, whatever
/// network the platform's own share tokens are issued on.
/// </remarks>
public sealed class CryptoPaymentOptions
{
    public const string SectionName = "CryptoPayments";

    /// <summary>Master switch for the deposit watcher.</summary>
    public bool WatcherEnabled { get; init; } = true;

    public int PollIntervalSeconds { get; init; } = 30;

    /// <summary>Tether on BNB Smart Chain (BEP-20).</summary>
    public string UsdtBep20Contract { get; init; } = "0x55d398326f99059fF775485246999027B3197955";

    /// <summary>Tether on TRON (TRC-20).</summary>
    public string UsdtTrc20Contract { get; init; } = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

    public string BscRpcUrl { get; init; } = "https://bsc-rpc.publicnode.com";

    /// <summary>Blocks a transfer must be buried under before it counts (~3 s per block).</summary>
    public int BscConfirmations { get; init; } = 15;

    /// <summary>Largest block window asked for in one eth_getLogs call.</summary>
    public int BscMaxBlockRange { get; init; } = 2_000;

    public string TronGridUrl { get; init; } = "https://api.trongrid.io";

    /// <summary>Optional TronGrid key; without one the public rate limit applies.</summary>
    public string? TronGridApiKey { get; init; }

    /// <summary>The token contract a method pays in, or null when it is not configured.</summary>
    public string? ContractFor(CryptoPaymentMethod method, PaymentSettings settings) => method switch
    {
        CryptoPaymentMethod.UsdtBep20 => UsdtBep20Contract,
        CryptoPaymentMethod.UsdtTrc20 => UsdtTrc20Contract,
        CryptoPaymentMethod.KgstBep20 => settings.KgstContractAddress,
        _ => null
    };
}
