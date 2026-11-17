namespace Atria.Domain.Payments;

/// <summary>A way an investor can pay for an application in crypto. Each is one token on one network.</summary>
public enum CryptoPaymentMethod
{
    /// <summary>Tether (USDT) on BNB Smart Chain, BEP-20.</summary>
    UsdtBep20 = 1,

    /// <summary>Tether (USDT) on TRON, TRC-20.</summary>
    UsdtTrc20 = 2,

    /// <summary>KGST — the som stablecoin (1 KGST = 1 KGS) on BNB Smart Chain.</summary>
    KgstBep20 = 3
}

public static class CryptoPaymentMethodExtensions
{
    /// <summary>Ticker shown to the investor.</summary>
    public static string Token(this CryptoPaymentMethod method) => method switch
    {
        CryptoPaymentMethod.UsdtBep20 or CryptoPaymentMethod.UsdtTrc20 => "USDT",
        CryptoPaymentMethod.KgstBep20 => "KGST",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    /// <summary>Network label shown to the investor (sending to the wrong network loses the money).</summary>
    public static string Network(this CryptoPaymentMethod method) => method switch
    {
        CryptoPaymentMethod.UsdtBep20 or CryptoPaymentMethod.KgstBep20 => "BEP-20",
        CryptoPaymentMethod.UsdtTrc20 => "TRC-20",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    /// <summary>Is the token pegged to the som, so no exchange rate applies?</summary>
    public static bool IsSomPegged(this CryptoPaymentMethod method) => method == CryptoPaymentMethod.KgstBep20;

    /// <summary>Wire name, stable across renames of the enum members.</summary>
    public static string ToWire(this CryptoPaymentMethod method) => method switch
    {
        CryptoPaymentMethod.UsdtBep20 => "usdt_bep20",
        CryptoPaymentMethod.UsdtTrc20 => "usdt_trc20",
        CryptoPaymentMethod.KgstBep20 => "kgst_bep20",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    public static bool TryParseWire(string? value, out CryptoPaymentMethod method)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "usdt_bep20": method = CryptoPaymentMethod.UsdtBep20; return true;
            case "usdt_trc20": method = CryptoPaymentMethod.UsdtTrc20; return true;
            case "kgst_bep20": method = CryptoPaymentMethod.KgstBep20; return true;
            default: method = default; return false;
        }
    }
}
