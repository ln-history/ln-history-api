using LN_history.Data.DataStores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LN_history.Core.Auth;

/// <summary>
/// Feeds and drains the in-memory <see cref="IApiAccessService"/>: loads keys + weights and seeds
/// today's spend at startup, reloads keys + weights every <c>ReloadSeconds</c>, and flushes usage
/// deltas to <c>api.usage</c> and buffered requests to <c>api.request_log</c> every <c>FlushSeconds</c>
/// (plus a final flush on shutdown). Only
/// registered when the API-key middleware is enabled.
/// </summary>
public sealed class ApiAccessMaintenanceService : BackgroundService
{
    private readonly IApiAccessService _access;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ApiAccessOptions _options;
    private readonly ILogger<ApiAccessMaintenanceService> _logger;

    public ApiAccessMaintenanceService(
        IApiAccessService access,
        IServiceScopeFactory scopeFactory,
        ApiAccessOptions options,
        ILogger<ApiAccessMaintenanceService> logger)
    {
        _access = access;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeAsync(stoppingToken);

        var flush = TimeSpan.FromSeconds(Math.Max(1, _options.FlushSeconds));
        var reloadEvery = TimeSpan.FromSeconds(Math.Max(_options.FlushSeconds, _options.ReloadSeconds));
        var sinceReload = TimeSpan.Zero;

        using var timer = new PeriodicTimer(flush);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);

                sinceReload += flush;
                if (sinceReload >= reloadEvery)
                {
                    sinceReload = TimeSpan.Zero;
                    await ReloadKeysAndWeightsAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await FlushAsync(CancellationToken.None); // best-effort final flush
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IApiKeyStore>();

            var keys = await store.LoadKeysAsync(cancellationToken);
            var weights = await store.LoadWeightsAsync(cancellationToken);
            _access.ApplyKeysAndWeights(keys, weights);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var spend = await store.LoadTodaySpendAsync(today, cancellationToken);
            _access.SeedTodaySpend(spend, today);

            _logger.LogInformation("API access initialized: {Keys} keys, {Weights} weights, {Spenders} keys with spend today.",
                keys.Count, weights.Count, spend.Count);
        }
        catch (Exception ex)
        {
            // Fail open only for the emergency admin: researcher keys simply won't resolve until a
            // later reload succeeds. Do not crash the host over a transient DB issue at startup.
            _logger.LogError(ex, "API access initialization failed; only the emergency admin key will work until reload succeeds.");
        }
    }

    private async Task ReloadKeysAndWeightsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IApiKeyStore>();
            var keys = await store.LoadKeysAsync(cancellationToken);
            var weights = await store.LoadWeightsAsync(cancellationToken);
            _access.ApplyKeysAndWeights(keys, weights);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API key/weight reload failed; keeping the previous in-memory set.");
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        await FlushUsageAsync(cancellationToken);
        await FlushRequestLogAsync(cancellationToken);
    }

    private async Task FlushRequestLogAsync(CancellationToken cancellationToken)
    {
        var dropped = _access.TakeDroppedRequestCount();
        if (dropped > 0)
            _logger.LogWarning("The request-log buffer was full; {Dropped} requests were not logged.", dropped);

        var entries = _access.DrainPendingRequests();
        if (entries.Count == 0) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IApiKeyStore>();
            await store.InsertRequestLogAsync(entries, cancellationToken);
        }
        catch (Exception ex)
        {
            // Same contract as the usage ledger: best-effort, enforcement is unaffected.
            _logger.LogWarning(ex, "Writing {Count} rows to api.request_log failed; they are dropped.", entries.Count);
        }
    }

    private async Task FlushUsageAsync(CancellationToken cancellationToken)
    {
        var deltas = _access.DrainPendingUsage();
        if (deltas.Count == 0) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IApiKeyStore>();
            await store.FlushUsageAsync(deltas, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort ledger: enforcement is in-memory and unaffected. Log the dropped batch.
            _logger.LogWarning(ex, "Flushing {Count} usage deltas to api.usage failed; they are dropped.", deltas.Count);
        }
    }
}
