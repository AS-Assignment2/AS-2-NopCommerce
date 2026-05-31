using System.Text;
using OrderIntegrationService.Configuration;
using RabbitMQ.Client;

namespace OrderIntegrationService.Services;

/// <summary>
/// Publishes events to the verdemart.events topic exchange.
/// Used by the webhook controller to emit stock.updated downstream.
/// </summary>
public class RabbitMqPublisher
{
    private readonly RabbitMqConfig _config;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(RabbitMqConfig config, ILogger<RabbitMqPublisher> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task PublishAsync(string routingKey, string messageBody)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config.Host,
            Port = _config.Port,
            UserName = _config.Username,
            Password = _config.Password
        };

        using var connection = await factory.CreateConnectionAsync();
        using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            exchange: _config.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        var body = Encoding.UTF8.GetBytes(messageBody);

        await channel.BasicPublishAsync(
            exchange: _config.Exchange,
            routingKey: routingKey,
            body: body);

        _logger.LogInformation("Published to RabbitMQ exchange={Exchange} routingKey={RoutingKey}", _config.Exchange, routingKey);
    }
}
