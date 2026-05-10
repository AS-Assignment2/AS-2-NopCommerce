namespace OsposAdapter.Models;

public class SaleCompletedEvent
{
    public string EventId { get; set; } = string.Empty;
    public string EventType => "sale.completed";
    public DateTime Timestamp { get; set; }
    public List<SaleItem> Items { get; set; } = new();
}

public class SaleItem
{
    public string Sku { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string StoreId { get; set; } = "main";
}
