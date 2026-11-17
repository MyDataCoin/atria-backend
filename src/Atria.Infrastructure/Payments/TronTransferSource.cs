using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atria.Application.Abstractions;
using Atria.Application.Payments;
using Atria.Domain.Payments;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Payments;

/// <summary>
/// Incoming TRC-20 USDT transfers on TRON mainnet, via TronGrid's account history (confirmed only).
/// The position is a millisecond block timestamp.
/// </summary>
public sealed class TronTransferSource : ICryptoTransferSource
{
    private const int PageSize = 200;

    private readonly HttpClient _http;
    private readonly CryptoPaymentOptions _options;

    public TronTransferSource(HttpClient http, IOptions<CryptoPaymentOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public bool Supports(CryptoPaymentMethod method) => method == CryptoPaymentMethod.UsdtTrc20;

    // A few minutes back rather than "this millisecond": the clock here and TRON's are not the same clock.
    public Task<long> GetStartPositionAsync(CryptoPaymentMethod method, CancellationToken ct)
        => Task.FromResult(DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds());

    public async Task<TransferPage> ReadAsync(
        CryptoPaymentMethod method, string tokenContract, string toAddress, long fromPosition, CancellationToken ct)
    {
        var url = $"{_options.TronGridUrl.TrimEnd('/')}/v1/accounts/{Uri.EscapeDataString(toAddress)}/transactions/trc20"
                  + $"?only_to=true&only_confirmed=true&limit={PageSize}&order_by=block_timestamp,asc"
                  + $"&contract_address={Uri.EscapeDataString(tokenContract)}&min_timestamp={fromPosition}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(_options.TronGridApiKey))
            request.Headers.Add("TRON-PRO-API-KEY", _options.TronGridApiKey);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await JsonSerializer.DeserializeAsync<TronPage>(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var rows = body?.Data ?? new List<TronTransfer>();

        var transfers = rows
            .Where(r => r.To is not null && string.Equals(r.To, toAddress, StringComparison.Ordinal)
                        && string.Equals(r.TokenInfo?.Address, tokenContract, StringComparison.Ordinal))
            .Select(r => new IncomingTransfer(
                r.TransactionId!,
                r.TransactionId!,
                r.From ?? string.Empty,
                r.To!,
                ToUnits(r.Value, r.TokenInfo?.Decimals ?? 6),
                DateTimeOffset.FromUnixTimeMilliseconds(r.BlockTimestamp).UtcDateTime))
            .ToList();

        // Resume from the newest timestamp seen. Same-millisecond rows are re-read next time and
        // deduplicated by transaction id, which is cheaper than risking a skipped transfer.
        var next = rows.Count == 0 ? fromPosition : Math.Max(fromPosition, rows.Max(r => r.BlockTimestamp));
        if (rows.Count < PageSize && rows.Count > 0)
            next++;

        return new TransferPage(transfers, next);
    }

    internal static decimal ToUnits(string? raw, int decimals)
    {
        if (!BigInteger.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return 0;

        var scale = BigInteger.Pow(10, decimals);
        var whole = BigInteger.DivRem(value, scale, out var remainder);
        return (decimal)whole + (decimal)remainder / (decimal)scale;
    }

    private sealed class TronPage
    {
        [JsonPropertyName("data")] public List<TronTransfer>? Data { get; set; }
    }

    private sealed class TronTransfer
    {
        [JsonPropertyName("transaction_id")] public string? TransactionId { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
        [JsonPropertyName("block_timestamp")] public long BlockTimestamp { get; set; }
        [JsonPropertyName("token_info")] public TronTokenInfo? TokenInfo { get; set; }
    }

    private sealed class TronTokenInfo
    {
        [JsonPropertyName("address")] public string? Address { get; set; }
        [JsonPropertyName("decimals")] public int Decimals { get; set; }
    }
}
