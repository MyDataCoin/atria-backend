using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Trading;

/// <summary>
/// The operator notifier in the state the platform is actually in: notices are assembled and
/// recorded, and there is nowhere to send them yet.
/// </summary>
/// <remarks>
/// <para>
/// Which operator will run secondary trading, and whether they take a webhook from us or expect us to
/// publish to a queue of theirs, is still open. Inventing a URL would produce a client that looks
/// configured and delivers nothing, so this reports honestly instead: the notice is built, logged in
/// full, and declared undelivered.
/// </para>
/// <para>
/// The notice is logged as its complete payload on purpose. Until the transport exists, the log is
/// the only record that the obligation arose, and an operator can hand a notice over by hand from it
/// — which is exactly what will be needed during the first weeks of a connection.
/// </para>
/// </remarks>
public sealed class UnconfiguredExchangeNotifier : IExchangeNotifier
{
    private readonly ExchangeOptions _options;
    private readonly ILogger<UnconfiguredExchangeNotifier> _logger;

    public UnconfiguredExchangeNotifier(
        IOptions<ExchangeOptions> options, ILogger<UnconfiguredExchangeNotifier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => false;

    public Task<bool> NotifyAsync(ExchangeNotice notice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notice);

        _logger.LogWarning(
            "Exchange notice {Kind} for issue {Property} ({Reference}) could not be delivered: no "
            + "trading operator endpoint is configured. Payload: {Payload}",
            notice.Kind, notice.PropertyId, notice.Reference, notice.Payload);

        return Task.FromResult(false);
    }
}
