using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using OsposAdapter.Configuration;
using OsposAdapter.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OsposAdapter.Services;

/// <summary>
/// Subscribes to `stock.updated` on the verdemart.events exchange (published by the
/// Order Integration Service once the WMS confirms a reservation) and mirrors the
/// warehouse on-hand back into OSPOS — the inbound half of the integration that lets
/// the POS reflect stock changes originating on other channels.
///
/// Reverse-maps productId N -> item_number "PN", writes the absolute quantity to
/// ospos_item_quantities, and records the change as an ospos_inventory row the same way
/// the OSPOS UI logs a manual quantity edit (the grid inner-joins inventory, so the row
/// is required for the adjustment to be visible there).
/// </summary>
public class StockUpdateConsumer : BackgroundService
{
    private const int StockLocationId = 1;
    private const int AdminUserId = 1;

    private readonly RabbitMqConfig _rabbitConfig;
    private readonly OsposConfig _osposConfig;
    private readonly IdempotencyTracker _tracker;
    private readonly ILogger<StockUpdateConsumer> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public StockUpdateConsumer(
        RabbitMqConfig rabbitConfig,
        OsposConfig osposConfig,
        IdempotencyTracker tracker,
        ILogger<StockUpdateConsumer> logger)
    {
        _rabbitConfig = rabbitConfig;
        _osposConfig = osposConfig;
        _tracker = tracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _tracker.EnsureStockEventsTableAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StockUpdateConsumer failed to start, retrying in 10s");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _rabbitConfig.Host,
            Port = _rabbitConfig.Port,
            UserName = _rabbitConfig.Username,
            Password = _rabbitConfig.Password
        };

        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(_rabbitConfig.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await _channel.QueueDeclareAsync(_rabbitConfig.StockUpdatedQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _channel.QueueBindAsync(_rabbitConfig.StockUpdatedQueue, _rabbitConfig.Exchange, _rabbitConfig.StockUpdatedRoutingKey, cancellationToken: ct);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 8, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageAsync;

        await _channel.BasicConsumeAsync(queue: _rabbitConfig.StockUpdatedQueue, autoAck: false, consumer: consumer, cancellationToken: ct);

        _logger.LogInformation(
            "StockUpdateConsumer subscribed to {Queue} (routingKey={RoutingKey})",
            _rabbitConfig.StockUpdatedQueue,
            _rabbitConfig.StockUpdatedRoutingKey
        );

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
    {
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            var ev = JsonSerializer.Deserialize<StockUpdatedEvent>(
                body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (ev is null || string.IsNullOrEmpty(ev.EventId) || ev.ProductId <= 0)
            {
                _logger.LogWarning("stock.updated: malformed payload, dropping. Body={Body}", body);
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            if (await _tracker.IsStockEventProcessedAsync(ev.EventId))
            {
                _logger.LogInformation("stock.updated: duplicate eventId={EventId}, skipping", ev.EventId);
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            await ApplyStockUpdateAsync(ev);

            await _tracker.MarkStockEventProcessedAsync(ev.EventId);
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "stock.updated: handler exception, nack-no-requeue. Body={Body}", body);
            try { await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false); }
            catch { /* ignored */ }
        }
    }

    private async Task ApplyStockUpdateAsync(StockUpdatedEvent ev)
    {
        var itemNumber = $"P{ev.ProductId}";

        await using var connection = new MySqlConnection(_osposConfig.ConnectionString);
        await connection.OpenAsync();

        var item = await connection.QuerySingleOrDefaultAsync<OsposItemRow>(@"
            SELECT i.item_id AS ItemId,
                   COALESCE(q.quantity, 0) AS CurrentQty
            FROM ospos_items i
            LEFT JOIN ospos_item_quantities q
                   ON q.item_id = i.item_id AND q.location_id = @locationId
            WHERE i.item_number = @itemNumber AND i.deleted = 0
            LIMIT 1",
            new { itemNumber, locationId = StockLocationId });

        if (item is null)
        {
            _logger.LogWarning(
                "stock.updated: no OSPOS item with item_number={ItemNumber} (productId={ProductId}), skipping",
                itemNumber, ev.ProductId);
            return;
        }

        decimal newQty = ev.NewStockQuantity;
        var delta = newQty - item.CurrentQty;

        if (delta == 0)
        {
            _logger.LogInformation("stock.updated: {ItemNumber} already at {Qty}, no change", itemNumber, newQty);
            return;
        }

        await using var tx = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync(@"
            INSERT INTO ospos_item_quantities (item_id, location_id, quantity)
            VALUES (@itemId, @locationId, @newQty)
            ON DUPLICATE KEY UPDATE quantity = @newQty",
            new { itemId = item.ItemId, locationId = StockLocationId, newQty }, tx);

        await connection.ExecuteAsync(@"
            INSERT INTO ospos_inventory
                (trans_items, trans_user, trans_date, trans_comment, trans_location, trans_inventory)
            VALUES (@itemId, @userId, NOW(), @comment, @locationId, @delta)",
            new
            {
                itemId = item.ItemId,
                userId = AdminUserId,
                locationId = StockLocationId,
                delta,
                comment = $"VerdeMart stock sync (eventId={ev.EventId})"
            }, tx);

        await tx.CommitAsync();

        _logger.LogInformation(
            "stock.updated: {ItemNumber} (item_id={ItemId}) {Old}->{New} (delta {Delta}), eventId={EventId}",
            itemNumber, item.ItemId, item.CurrentQty, newQty, delta, ev.EventId);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { if (_channel is not null) await _channel.DisposeAsync(); } catch { /* ignored */ }
        try { if (_connection is not null) await _connection.DisposeAsync(); } catch { /* ignored */ }
        await base.StopAsync(cancellationToken);
    }

    private sealed class OsposItemRow
    {
        public int ItemId { get; set; }
        public decimal CurrentQty { get; set; }
    }
}
