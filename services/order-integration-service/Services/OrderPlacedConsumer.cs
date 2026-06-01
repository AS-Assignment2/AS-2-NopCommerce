using System.Text;
using System.Text.Json;
using OrderIntegrationService.Configuration;
using OrderIntegrationService.Models;
using Polly.CircuitBreaker;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderIntegrationService.Services;

/// <summary>
/// Subscribes to `order.placed` on the verdemart.events exchange. For each
/// message: dedupe, forward to ERP (retry policy), forward to WMS (circuit
/// breaker), DLQ on failure.
/// </summary>
public class OrderPlacedConsumer : BackgroundService
{
    private readonly RabbitMqConfig _config;
    private readonly IServiceProvider _services;
    private readonly ILogger<OrderPlacedConsumer> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public OrderPlacedConsumer(
        RabbitMqConfig config,
        IServiceProvider services,
        ILogger<OrderPlacedConsumer> logger)
    {
        _config = config;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrderPlacedConsumer failed to start, retrying in 10s");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config.Host,
            Port = _config.Port,
            UserName = _config.Username,
            Password = _config.Password
        };

        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(_config.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await _channel.QueueDeclareAsync(_config.OrderPlacedQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _channel.QueueBindAsync(_config.OrderPlacedQueue, _config.Exchange, "order.placed", cancellationToken: ct);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 8, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageAsync;

        await _channel.BasicConsumeAsync(queue: _config.OrderPlacedQueue, autoAck: false, consumer: consumer, cancellationToken: ct);

        _logger.LogInformation("OrderPlacedConsumer subscribed to {Queue}", _config.OrderPlacedQueue);

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
    {
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());
        using var scope = _services.CreateScope();
        var idempotency = scope.ServiceProvider.GetRequiredService<IdempotencyTracker>();
        var dlq = scope.ServiceProvider.GetRequiredService<DeadLetterQueue>();
        var health = scope.ServiceProvider.GetRequiredService<HealthState>();
        var erp = scope.ServiceProvider.GetRequiredService<ErpAdapter>();
        var wms = scope.ServiceProvider.GetRequiredService<WmsAdapter>();

        try
        {
            var ev = JsonSerializer.Deserialize<OrderPlacedEvent>(body);
            if (ev is null || string.IsNullOrEmpty(ev.EventId))
            {
                _logger.LogWarning("order.placed: malformed payload, dropping. Body={Body}", body);
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            if (!idempotency.TryMark(ev.EventId))
            {
                health.MarkDuplicate();
                _logger.LogInformation("order.placed: duplicate eventId={EventId}, skipping", ev.EventId);
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            var erpPayload = ErpAdapter.BuildOrderPayload(ev);
            var wmsPayload = WmsAdapter.BuildReservationPayload(ev);

            // ERP forwarding (retry policy applies inside HttpClient)
            var erpOk = await erp.PostRawAsync("/orders", erpPayload, CancellationToken.None);
            if (!erpOk)
            {
                dlq.Enqueue(new DlqEntry
                {
                    EventId = ev.EventId,
                    EventType = "order.placed",
                    Target = "ERP",
                    Payload = erpPayload,
                    FirstFailedAt = DateTime.UtcNow,
                    LastAttemptAt = DateTime.UtcNow,
                    RetryCount = 0,
                    LastError = "ERP forwarding failed after retries"
                });
            }

            // WMS forwarding (circuit breaker applies)
            try
            {
                var (wmsOk, error) = await wms.PostRawAsync("/reservations", wmsPayload, CancellationToken.None);
                if (!wmsOk)
                {
                    dlq.Enqueue(new DlqEntry
                    {
                        EventId = ev.EventId,
                        EventType = "order.placed",
                        Target = "WMS",
                        Payload = wmsPayload,
                        FirstFailedAt = DateTime.UtcNow,
                        LastAttemptAt = DateTime.UtcNow,
                        RetryCount = 0,
                        LastError = error ?? "WMS failure"
                    });
                }
            }
            catch (BrokenCircuitException)
            {
                health.CircuitState = "OPEN";
                dlq.Enqueue(new DlqEntry
                {
                    EventId = ev.EventId,
                    EventType = "order.placed",
                    Target = "WMS",
                    Payload = wmsPayload,
                    FirstFailedAt = DateTime.UtcNow,
                    LastAttemptAt = DateTime.UtcNow,
                    RetryCount = 0,
                    LastError = "Circuit OPEN"
                });
                _logger.LogWarning("order.placed: WMS circuit OPEN, routed to DLQ. eventId={EventId}", ev.EventId);
            }

            health.MarkProcessed();
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "order.placed: handler exception, nack-no-requeue. Body={Body}", body);
            try { await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false); }
            catch { /* ignored */ }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { if (_channel != null) await _channel.CloseAsync(cancellationToken); } catch { }
        try { if (_connection != null) await _connection.CloseAsync(cancellationToken); } catch { }
        await base.StopAsync(cancellationToken);
    }
}
