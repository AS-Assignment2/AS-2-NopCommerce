namespace OsposAdapter.Configuration;

public class AdapterConfig
{
    public int PollingIntervalSeconds { get; set; } = 30;
    public string IdempotencyDbPath { get; set; } = "/app/data/idempotency.db";
}
