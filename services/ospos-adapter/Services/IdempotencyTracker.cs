using Microsoft.Data.Sqlite;
using Dapper;
using OsposAdapter.Configuration;

namespace OsposAdapter.Services;

public class IdempotencyTracker
{
    private readonly AdapterConfig _config;
    private readonly ILogger<IdempotencyTracker> _logger;

    public IdempotencyTracker(AdapterConfig config, ILogger<IdempotencyTracker> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_config.IdempotencyDbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created idempotency database directory: {Directory}", directory);
            }

            using var connection = new SqliteConnection($"Data Source={_config.IdempotencyDbPath}");
            await connection.OpenAsync();

            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS ProcessedSales (
                    SaleId INTEGER PRIMARY KEY,
                    ProcessedAt TEXT NOT NULL
                )");

            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS LastProcessedTime (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Timestamp TEXT NOT NULL
                )");

            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS ProcessedStockEvents (
                    EventId TEXT PRIMARY KEY,
                    ProcessedAt TEXT NOT NULL
                )");

            var count = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM LastProcessedTime WHERE Id = 1"
            );

            if (count == 0)
            {
                var initialTime = DateTime.UtcNow.AddHours(-1).ToString("o");
                await connection.ExecuteAsync(
                    "INSERT INTO LastProcessedTime (Id, Timestamp) VALUES (1, @Timestamp)",
                    new { Timestamp = initialTime }
                );
                _logger.LogInformation("Initialized last processed time to: {Time}", initialTime);
            }

            _logger.LogInformation("Idempotency database initialized at {Path}", _config.IdempotencyDbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize idempotency database");
            throw;
        }
    }

    public async Task<bool> IsProcessedAsync(int saleId)
    {
        using var connection = new SqliteConnection($"Data Source={_config.IdempotencyDbPath}");
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ProcessedSales WHERE SaleId = @SaleId",
            new { SaleId = saleId }
        );
        return count > 0;
    }

    public async Task MarkProcessedAsync(int saleId)
    {
        using var connection = new SqliteConnection($"Data Source={_config.IdempotencyDbPath}");
        await connection.ExecuteAsync(
            "INSERT OR IGNORE INTO ProcessedSales (SaleId, ProcessedAt) VALUES (@SaleId, @ProcessedAt)",
            new { SaleId = saleId, ProcessedAt = DateTime.UtcNow.ToString("o") }
        );
    }

    public async Task<DateTime> GetLastProcessedTimeAsync()
    {
        using var connection = new SqliteConnection($"Data Source={_config.IdempotencyDbPath}");
        var timestamp = await connection.ExecuteScalarAsync<string>(
            "SELECT Timestamp FROM LastProcessedTime WHERE Id = 1"
        );
        return DateTime.Parse(timestamp ?? DateTime.UtcNow.AddHours(-1).ToString("o"));
    }

    public async Task UpdateLastProcessedTimeAsync(DateTime time)
    {
        using var connection = new SqliteConnection($"Data Source={_config.IdempotencyDbPath}");
        await connection.ExecuteAsync(
            "UPDATE LastProcessedTime SET Timestamp = @Timestamp WHERE Id = 1",
            new { Timestamp = time.ToString("o") }
        );
    }

    // Stock-event dedupe lives here too so the inbound StockUpdateConsumer can run
    // independently of the polling service's InitializeAsync (this only touches its
    // own table — no race with the LastProcessedTime seed).
    public async Task EnsureStockEventsTableAsync()
    {
        var directory = Path.GetDirectoryName(_config.IdempotencyDbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection($"Data Source={_config.IdempotencyDbPath}");
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ProcessedStockEvents (
                EventId TEXT PRIMARY KEY,
                ProcessedAt TEXT NOT NULL
            )");
    }

    public async Task<bool> IsStockEventProcessedAsync(string eventId)
    {
        using var connection = new SqliteConnection($"Data Source={_config.IdempotencyDbPath}");
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ProcessedStockEvents WHERE EventId = @EventId",
            new { EventId = eventId }
        );
        return count > 0;
    }

    public async Task MarkStockEventProcessedAsync(string eventId)
    {
        using var connection = new SqliteConnection($"Data Source={_config.IdempotencyDbPath}");
        await connection.ExecuteAsync(
            "INSERT OR IGNORE INTO ProcessedStockEvents (EventId, ProcessedAt) VALUES (@EventId, @ProcessedAt)",
            new { EventId = eventId, ProcessedAt = DateTime.UtcNow.ToString("o") }
        );
    }
}
