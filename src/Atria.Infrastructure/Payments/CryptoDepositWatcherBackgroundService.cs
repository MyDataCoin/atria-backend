using Atria.Application.Abstractions;
using Atria.Application.Payments;
using Atria.Application.Payments.Services;
using Atria.Domain.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Payments;

/// <summary>
/// Polls the receiving wallets of every enabled crypto method and hands new transfers to
/// <see cref="CryptoDepositProcessor"/>. One method failing (node down, rate limit) never stops the
/// others; its cursor simply stays put and the range is read again next tick.
/// </summary>
public sealed class CryptoDepositWatcherBackgroundService : BackgroundService
{
    // Pages per method per tick: enough to catch up after a short outage without hogging the node.
    private const int MaxPagesPerTick = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CryptoPaymentOptions _options;
    private readonly ILogger<CryptoDepositWatcherBackgroundService> _logger;

    public CryptoDepositWatcherBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<CryptoPaymentOptions> options,
        ILogger<CryptoDepositWatcherBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WatcherEnabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds)));
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Crypto deposit watch failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        IReadOnlyList<CryptoPaymentMethod> methods;
        PaymentSettings? settings;
        using (var scope = _scopeFactory.CreateScope())
        {
            settings = await scope.ServiceProvider.GetRequiredService<IPaymentSettingsRepository>().GetAsync(ct);
            methods = settings?.EnabledMethods() ?? Array.Empty<CryptoPaymentMethod>();
        }

        foreach (var method in methods)
        {
            try
            {
                await WatchAsync(method, settings!, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Reading {Method} deposits failed; retrying next tick.", method.ToWire());
            }
        }
    }

    private async Task WatchAsync(CryptoPaymentMethod method, PaymentSettings settings, CancellationToken ct)
    {
        var address = settings.AddressFor(method)!;
        var contract = _options.ContractFor(method, settings);
        if (contract is null)
            return;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var source = sp.GetServices<ICryptoTransferSource>().FirstOrDefault(s => s.Supports(method));
        if (source is null)
            return;

        var cursors = sp.GetRequiredService<ICryptoWatchCursorRepository>();
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var processor = sp.GetRequiredService<CryptoDepositProcessor>();

        var key = CryptoWatchCursor.KeyFor(method, address);
        var cursor = await cursors.FindAsync(key, ct);
        if (cursor is null)
        {
            cursor = CryptoWatchCursor.Start(key, await source.GetStartPositionAsync(method, ct));
            await cursors.AddAsync(cursor, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        for (var page = 0; page < MaxPagesPerTick; page++)
        {
            var from = cursor.Position;
            var result = await source.ReadAsync(method, contract, address, from, ct);

            var outcome = await processor.ProcessAsync(method, result.Transfers, ct);
            if (outcome.Matched > 0)
                _logger.LogInformation(
                    "{Method}: {Matched} payment(s) matched, {Recorded} transfer(s) recorded.",
                    method.ToWire(), outcome.Matched, outcome.Recorded);

            cursor.Advance(result.NextPosition);
            await unitOfWork.SaveChangesAsync(ct);

            if (result.NextPosition <= from)
                break;
        }
    }
}
