using OsposAdapter.Configuration;

namespace OsposAdapter.Services;

public class OsposPollingService : BackgroundService
{
    private readonly AdapterConfig _adapterConfig;
    private readonly OsposConfig _osposConfig;
    private readonly RabbitMqPublisher _publisher;
    private readonly IdempotencyTracker _tracker;
    private readonly ILogger<OsposPollingService> _logger;

    public OsposPollingService(
        AdapterConfig adapterConfig,
        OsposConfig osposConfig,
        RabbitMqPublisher publisher,
        IdempotencyTracker tracker,
        ILogger<OsposPollingService> logger)
    {
        _adapterConfig = adapterConfig;
        _osposConfig = osposConfig;
        _publisher = publisher;
        _tracker = tracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OSPOS Polling Service starting...");
        _logger.LogInformation("Polling interval: {Interval} seconds", _adapterConfig.PollingIntervalSeconds);
        _logger.LogInformation("OSPOS Database: {ConnectionString}", _osposConfig.ConnectionString);

        await _tracker.InitializeAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndPublishAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling cycle failed");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_adapterConfig.PollingIntervalSeconds),
                stoppingToken
            );
        }

        _logger.LogInformation("OSPOS Polling Service stopped");
    }

    private async Task PollAndPublishAsync()
    {
        _logger.LogInformation("Polling OSPOS for new sales...");

        _logger.LogInformation("Polling cycle complete (stub - no actual queries yet)");

        await Task.CompletedTask;
    }
}
