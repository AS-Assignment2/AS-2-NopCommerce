using System.Text;
using RabbitMQ.Client;
using OsposAdapter.Configuration;

namespace OsposAdapter.Services;

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
        try
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
                autoDelete: false
            );

            var body = Encoding.UTF8.GetBytes(messageBody);

            await channel.BasicPublishAsync(
                exchange: _config.Exchange,
                routingKey: routingKey,
                body: body
            );

            _logger.LogInformation(
                "Published message to RabbitMQ: exchange={Exchange}, routingKey={RoutingKey}",
                _config.Exchange,
                routingKey
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to RabbitMQ");
            throw;
        }
    }
}
