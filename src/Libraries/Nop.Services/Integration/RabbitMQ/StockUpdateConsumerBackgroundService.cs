using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nop.Core.Configuration;
using Nop.Services.Catalog;
using Nop.Services.Integration;
using Nop.Services.Logging;
using Nop.Services.Orders;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Nop.Services.Integration.RabbitMQ;

/// <summary>
/// Subscribes to RabbitMQ `stock.updated` events emitted by the WMS via the Order
/// Integration Service and applies the inventory change to the nopCommerce catalog.
/// </summary>
public partial class StockUpdateConsumerBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IntegrationConfig _integrationConfig;

    private IConnection _connection;
    private IModel _channel;

    public StockUpdateConsumerBackgroundService(
        IServiceProvider serviceProvider,
        IntegrationConfig integrationConfig)
    {
        _serviceProvider = serviceProvider;
        _integrationConfig = integrationConfig;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(() => StartWithRetryAsync(stoppingToken), stoppingToken);
        return Task.CompletedTask;
    }

    private async Task StartWithRetryAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                StartConsuming();
                return;
            }
            catch (Exception ex)
            {
                using var scope = _serviceProvider.CreateScope();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger>();
                await logger.ErrorAsync($"StockUpdateConsumer: failed to start, retrying in 10s: {ex.Message}", ex);

                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }
    }

    private void StartConsuming()
    {
        var factory = new ConnectionFactory
        {
            HostName = _integrationConfig.RabbitMqHostname,
            Port = _integrationConfig.RabbitMqPort,
            UserName = _integrationConfig.RabbitMqUsername,
            Password = _integrationConfig.RabbitMqPassword,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(
            exchange: _integrationConfig.EventsExchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: _integrationConfig.StockUpdatedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false);

        _channel.QueueBind(
            queue: _integrationConfig.StockUpdatedQueue,
            exchange: _integrationConfig.EventsExchange,
            routingKey: _integrationConfig.StockUpdatedRoutingKey);

        _channel.BasicQos(prefetchSize: 0, prefetchCount: 8, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageAsync;

        _channel.BasicConsume(
            queue: _integrationConfig.StockUpdatedQueue,
            autoAck: false,
            consumer: consumer);
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
    {
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());

        using var scope = _serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger>();

        try
        {
            var payload = JsonSerializer.Deserialize<StockUpdatedPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload == null || payload.ProductId <= 0)
            {
                await logger.WarningAsync($"StockUpdateConsumer: invalid payload, dropping. Body={body}");
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }

            var handler = scope.ServiceProvider.GetRequiredService<IStockUpdateHandler>();
            await handler.HandleAsync(payload);

            _channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            await logger.ErrorAsync($"StockUpdateConsumer: handler failed: {ex.Message}. Body={body}", ex);
            _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { _channel?.Close(); } catch { /* ignored */ }
        try { _connection?.Close(); } catch { /* ignored */ }
        await base.StopAsync(cancellationToken);
    }
}

