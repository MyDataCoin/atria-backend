using Atria.Domain.Common;

namespace Atria.Domain.Payments;

/// <summary>
/// How far the deposit watcher has read one (method, wallet) pair: a block number on BNB, a
/// millisecond timestamp on TRON. Keyed by wallet too, so changing the wallet starts a fresh read.
/// </summary>
public sealed class CryptoWatchCursor : AggregateRoot
{
    public const int MaxKeyLength = 120;

    public string Key { get; private set; } = null!;
    public long Position { get; private set; }

    private CryptoWatchCursor() { }

    public static CryptoWatchCursor Start(string key, long position)
        => new() { Id = Guid.NewGuid(), Key = key, Position = position };

    public void Advance(long position)
    {
        if (position > Position)
            Position = position;
    }

    public static string KeyFor(CryptoPaymentMethod method, string address)
        => $"{method.ToWire()}:{address.ToLowerInvariant()}";
}
