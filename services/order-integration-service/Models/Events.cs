using System.Text.Json.Serialization;

namespace OrderIntegrationService.Models;

public class OrderPlacedEvent
{
    [JsonPropertyName("eventId")] public string EventId { get; set; } = string.Empty;
    [JsonPropertyName("orderId")] public int OrderId { get; set; }
    [JsonPropertyName("customerId")] public int CustomerId { get; set; }
    [JsonPropertyName("storeId")] public int StoreId { get; set; }
    [JsonPropertyName("total")] public decimal Total { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = "EUR";
    [JsonPropertyName("createdOnUtc")] public DateTime CreatedOnUtc { get; set; }
    [JsonPropertyName("items")] public List<OrderPlacedItem> Items { get; set; } = new();
}

public class OrderPlacedItem
{
    [JsonPropertyName("productId")] public int ProductId { get; set; }
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
    [JsonPropertyName("unitPrice")] public decimal UnitPrice { get; set; }
}

public class SaleCompletedEvent
{
    [JsonPropertyName("eventId")] public string EventId { get; set; } = string.Empty;
    [JsonPropertyName("eventType")] public string EventType { get; set; } = "sale.completed";
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
    [JsonPropertyName("items")] public List<SaleItem> Items { get; set; } = new();
}

public class SaleItem
{
    [JsonPropertyName("sku")] public string Sku { get; set; } = string.Empty;
    [JsonPropertyName("quantity")] public decimal Quantity { get; set; }
    [JsonPropertyName("storeId")] public string StoreId { get; set; } = "main";
}

public class StockChangedWebhook
{
    [JsonPropertyName("eventId")] public string EventId { get; set; } = string.Empty;
    [JsonPropertyName("productId")] public int ProductId { get; set; }
    [JsonPropertyName("warehouseId")] public int WarehouseId { get; set; }
    [JsonPropertyName("newStockQuantity")] public int NewStockQuantity { get; set; }
    [JsonPropertyName("occurredAt")] public string OccurredAt { get; set; } = string.Empty;
}

public class IntegrationHealth
{
    [JsonPropertyName("status")] public string Status { get; set; } = "healthy";
    [JsonPropertyName("circuitBreakerState")] public string CircuitBreakerState { get; set; } = "CLOSED";
    [JsonPropertyName("dlqDepth")] public int DlqDepth { get; set; }
    [JsonPropertyName("lastProcessedAt")] public string? LastProcessedAt { get; set; }
    [JsonPropertyName("eventsProcessed")] public long EventsProcessed { get; set; }
    [JsonPropertyName("duplicatesSkipped")] public long DuplicatesSkipped { get; set; }
}
