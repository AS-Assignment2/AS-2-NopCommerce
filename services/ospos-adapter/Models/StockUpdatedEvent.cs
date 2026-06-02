namespace OsposAdapter.Models;

/// <summary>
/// Payload of `stock.updated` as published by the Order Integration Service
/// (/webhooks/stock-changed). NewStockQuantity is the absolute on-hand at the WMS,
/// so applying it is idempotent regardless of how many times the event is delivered.
/// </summary>
public class StockUpdatedEvent
{
    public string EventId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int NewStockQuantity { get; set; }
    public string? OccurredAt { get; set; }
}
