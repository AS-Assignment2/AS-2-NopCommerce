using System.Text.Json;
using Dapper;
using MySqlConnector;
using OsposAdapter.Configuration;
using OsposAdapter.Models;

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
        var lastProcessedTime = await _tracker.GetLastProcessedTimeAsync();
        _logger.LogInformation("Polling OSPOS for sales after {LastTime}", lastProcessedTime);

        var sales = await QueryOsposSalesAsync(lastProcessedTime);
        var salesList = sales.ToList();

        if (salesList.Count == 0)
        {
            _logger.LogInformation("No new sales found");
            return;
        }

        _logger.LogInformation("Found {Count} sale line items to process", salesList.Count);

        var salesGroupedById = salesList
            .GroupBy(s => s.SaleId)
            .OrderBy(g => g.First().SaleTime);

        foreach (var saleGroup in salesGroupedById)
        {
            var saleId = saleGroup.Key;

            if (await _tracker.IsProcessedAsync(saleId))
            {
                _logger.LogInformation("Sale {SaleId} already processed, skipping", saleId);
                continue;
            }

            var saleEvent = BuildSaleCompletedEvent(saleGroup);
            var messageBody = JsonSerializer.Serialize(saleEvent);

            await _publisher.PublishAsync("sale.completed", messageBody);
            await _tracker.MarkProcessedAsync(saleId);
            await _tracker.UpdateLastProcessedTimeAsync(saleGroup.First().SaleTime);

            _logger.LogInformation(
                "Published sale.completed for OSPOS sale {SaleId} with {ItemCount} items",
                saleId,
                saleEvent.Items.Count
            );
        }
    }

    private async Task<IEnumerable<OsposSale>> QueryOsposSalesAsync(DateTime lastProcessedTime)
    {
        using var connection = new MySqlConnection(_osposConfig.ConnectionString);
        return await connection.QueryAsync<OsposSale>(@"
            SELECT
                s.sale_id AS SaleId,
                s.sale_time AS SaleTime,
                i.item_number AS Sku,
                si.quantity_purchased AS Quantity
            FROM ospos_sales s
            JOIN ospos_sales_items si ON s.sale_id = si.sale_id
            JOIN ospos_items i ON si.item_id = i.item_id
            WHERE s.sale_time > @lastProcessedTime
              AND s.sale_status = 0 -- OSPOS Constants.php: COMPLETED = 0 (1 = suspended)
            ORDER BY s.sale_time ASC
            LIMIT 100",
            new { lastProcessedTime }
        );
    }

    private static SaleCompletedEvent BuildSaleCompletedEvent(IGrouping<int, OsposSale> saleGroup)
    {
        var firstItem = saleGroup.First();
        return new SaleCompletedEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = firstItem.SaleTime,
            Items = saleGroup.Select(s => new SaleItem
            {
                Sku = s.Sku,
                Quantity = s.Quantity,
                StoreId = "main"
            }).ToList()
        };
    }
}
