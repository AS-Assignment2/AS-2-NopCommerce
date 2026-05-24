namespace Nop.Services.Integration;

/// <summary>
/// Payload of `stock.updated` events emitted by the WMS via the Order Integration Service.
/// </summary>
public partial class StockUpdatedPayload
{
    public string EventId { get; set; }
    public int ProductId { get; set; }
    public int NewStockLevel { get; set; }
    public int Delta { get; set; }
    public string Source { get; set; }
    public DateTime Timestamp { get; set; }
}
