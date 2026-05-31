using OrderIntegrationService.Configuration;
using OrderIntegrationService.Models;
using Polly.CircuitBreaker;

namespace OrderIntegrationService.Services;

/// <summary>
/// Background loop that drains the DLQ. Pops one entry, retries against the
/// failing target. On success → entry removed. On circuit OPEN → entry
/// re-enqueued and the loop sleeps. The Polly circuit breaker on the WMS
/// HttpClient transitions OPEN → HALF_OPEN automatically after 30s; the first
/// HALF_OPEN trial happens on the next reconciliation tick.
/// </summary>
public class ReconciliationService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly DeadLetterQueue _dlq;
    private readonly HealthState _health;
    private readonly ReconciliationConfig _config;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        IServiceProvider services,
        DeadLetterQueue dlq,
        HealthState health,
        ReconciliationConfig config,
        ILogger<ReconciliationService> logger)
    {
        _services = services;
        _dlq = dlq;
        _health = health;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _config.IntervalSeconds));
        _logger.LogInformation("Reconciliation loop started, interval={Interval}s", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation tick failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    private async Task DrainOnceAsync(CancellationToken ct)
    {
        var initialDepth = _dlq.Depth;
        if (initialDepth == 0) return;

        using var scope = _services.CreateScope();
        var erp = scope.ServiceProvider.GetRequiredService<ErpAdapter>();
        var wms = scope.ServiceProvider.GetRequiredService<WmsAdapter>();

        for (var i = 0; i < initialDepth; i++)
        {
            if (!_dlq.TryDequeue(out var entry)) return;

            var ok = await RetryAsync(entry, erp, wms, ct);
            if (ok)
            {
                _logger.LogInformation("Reconciled DLQ entry eventId={EventId} target={Target} after {Retries} retries",
                    entry.EventId, entry.Target, entry.RetryCount);
                _health.MarkProcessed();
            }
            else
            {
                entry.RetryCount += 1;
                entry.LastAttemptAt = DateTime.UtcNow;
                _dlq.Enqueue(entry);
                _logger.LogDebug("DLQ entry still failing eventId={EventId}, re-enqueued (count={Count})",
                    entry.EventId, entry.RetryCount);
                return;
            }
        }
    }

    private async Task<bool> RetryAsync(DlqEntry entry, ErpAdapter erp, WmsAdapter wms, CancellationToken ct)
    {
        try
        {
            if (entry.Target == "WMS")
            {
                var (ok, error) = await wms.PostRawAsync("/reservations", entry.Payload, ct);
                if (!ok) entry.LastError = error ?? "WMS failed";
                return ok;
            }
            if (entry.Target == "ERP")
            {
                var ok = await erp.PostRawAsync("/orders", entry.Payload, ct);
                if (!ok) entry.LastError = "ERP failed";
                return ok;
            }
            return false;
        }
        catch (BrokenCircuitException)
        {
            _health.CircuitState = "OPEN";
            entry.LastError = "Circuit OPEN";
            return false;
        }
        catch (Exception ex)
        {
            entry.LastError = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }
}
