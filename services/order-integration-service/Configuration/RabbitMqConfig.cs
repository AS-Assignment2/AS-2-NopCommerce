namespace OrderIntegrationService.Configuration;

public class RabbitMqConfig
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = "verdemart.events";
    public string OrderPlacedQueue { get; set; } = "order-integration.order-placed";
    public string SaleCompletedQueue { get; set; } = "order-integration.sale-completed";
    public string StockUpdatedRoutingKey { get; set; } = "stock.updated";
}
