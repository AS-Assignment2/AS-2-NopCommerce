using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nop.Core.Configuration;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Nop.Web.Framework.Infrastructure;

/// <summary>
/// Subscribes to `stock.updated` events on the verdemart.events exchange
/// (published by the Order Integration Service after WMS confirms a reservation)
/// and applies the delta to nopCommerce's product inventory.
/// </summary>
public partial class StockUpdateConsumerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegrationConfig _integrationConfig;
    private readonly ILogger _logger;

    private IConnection _connection;
    private IModel _channel;

    public StockUpdateConsumerBackgroundService(
        IServiceScopeFactory scopeFactory,
        IntegrationConfig integrationConfig,
        ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _integrationConfig = integrationConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Do not touch RabbitMQ or the database until nopCommerce is installed.
        // Before installation there is no connection string, so any DB write
        // (including error logging) throws and — with the default StopHost
        // behaviour — would take the whole web host down before the install
        // wizard can even be reached. Wait for the install to complete instead.
        while (!DataSettingsManager.IsDatabaseInstalled())
        {
            if (stoppingToken.IsCancellationRequested)
                return;
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        try
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

            _channel.ExchangeDeclare(_integrationConfig.EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false);
            _channel.QueueDeclare(_integrationConfig.StockUpdatedQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(_integrationConfig.StockUpdatedQueue, _integrationConfig.EventsExchange, _integrationConfig.StockUpdatedRoutingKey);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += OnMessageAsync;

            _channel.BasicConsume(_integrationConfig.StockUpdatedQueue, autoAck: false, consumer);
        }
        catch (Exception ex)
        {
            // Never log a startup failure to the DB here — a transient broker
            // problem must not be able to crash the host. Console only.
            Console.Error.WriteLine($"[StockUpdateConsumer] failed to start: {ex}");
        }
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        var json = Encoding.UTF8.GetString(args.Body.ToArray());

        try
        {
            var evt = JsonSerializer.Deserialize<StockUpdatedPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (evt == null || evt.ProductId <= 0)
            {
                _channel.BasicAck(args.DeliveryTag, multiple: false);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

            var product = await productService.GetProductByIdAsync(evt.ProductId);
            if (product == null)
            {
                await _logger.WarningAsync($"StockUpdateConsumer: unknown productId={evt.ProductId}, dropping");
                _channel.BasicAck(args.DeliveryTag, multiple: false);
                return;
            }

            await productService.AdjustInventoryAsync(product, evt.Delta,
                message: $"stock.updated from {evt.Source ?? "external"} (eventId={evt.EventId})");

            _channel.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            try { await _logger.ErrorAsync($"StockUpdateConsumer: failed to process message: {ex.Message}. Body: {json}", ex); }
            catch { Console.Error.WriteLine($"[StockUpdateConsumer] failed to process message: {ex}"); }
            try { _channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false); } catch { }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try { _channel?.Close(); } catch { }
        try { _connection?.Close(); } catch { }
        return base.StopAsync(cancellationToken);
    }

    private sealed class StockUpdatedPayload
    {
        public string EventId { get; set; }
        public int ProductId { get; set; }
        public int Delta { get; set; }
        public string Source { get; set; }
        public DateTime TimestampUtc { get; set; }
    }
}
