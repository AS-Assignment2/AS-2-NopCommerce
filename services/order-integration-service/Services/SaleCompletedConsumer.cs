using System.Text;
using System.Text.Json;
using OrderIntegrationService.Configuration;
using OrderIntegrationService.Models;
using Polly.CircuitBreaker;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderIntegrationService.Services;

/// <summary>
/// Subscribes to `sale.completed` from the OSPOS Adapter. Same flow as
/// OrderPlacedConsumer: dedupe → ERP → WMS → DLQ on circuit open.
/// </summary>
public class SaleCompletedConsumer : BackgroundService
{
    private readonly RabbitMqConfig _config;
    private readonly IServiceProvider _services;
    private readonly ILogger<SaleCompletedConsumer> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public SaleCompletedConsumer(
        RabbitMqConfig config,
        IServiceProvider services,
        ILogger<SaleCompletedConsumer> logger)
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
                _logger.LogError(ex, "SaleCompletedConsumer failed to start, retrying in 10s");
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
        await _channel.QueueDeclareAsync(_config.SaleCompletedQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _channel.QueueBindAsync(_config.SaleCompletedQueue, _config.Exchange, "sale.completed", cancellationToken: ct);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 8, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageAsync;

        await _channel.BasicConsumeAsync(queue: _config.SaleCompletedQueue, autoAck: false, consumer: consumer, cancellationToken: ct);

        _logger.LogInformation("SaleCompletedConsumer subscribed to {Queue}", _config.SaleCompletedQueue);

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
            var ev = JsonSerializer.Deserialize<SaleCompletedEvent>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (ev is null || string.IsNullOrEmpty(ev.EventId))
            {
                _logger.LogWarning("sale.completed: malformed payload, dropping. Body={Body}", body);
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            if (!idempotency.TryMark(ev.EventId))
            {
                health.MarkDuplicate();
                _logger.LogInformation("sale.completed: duplicate eventId={EventId}, skipping", ev.EventId);
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            var erpPayload = ErpAdapter.BuildSalePayload(ev);
            var wmsPayload = WmsAdapter.BuildSalePayload(ev);

            var erpOk = await erp.PostRawAsync("/orders", erpPayload, CancellationToken.None);
            if (!erpOk)
            {
                dlq.Enqueue(new DlqEntry
                {
                    EventId = ev.EventId,
                    EventType = "sale.completed",
                    Target = "ERP",
                    Payload = erpPayload,
                    FirstFailedAt = DateTime.UtcNow,
                    LastAttemptAt = DateTime.UtcNow,
                    LastError = "ERP forwarding failed after retries"
                });
            }

            try
            {
                var (wmsOk, error) = await wms.PostRawAsync("/reservations", wmsPayload, CancellationToken.None);
                if (!wmsOk)
                {
                    dlq.Enqueue(new DlqEntry
                    {
                        EventId = ev.EventId,
                        EventType = "sale.completed",
                        Target = "WMS",
                        Payload = wmsPayload,
                        FirstFailedAt = DateTime.UtcNow,
                        LastAttemptAt = DateTime.UtcNow,
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
                    EventType = "sale.completed",
                    Target = "WMS",
                    Payload = wmsPayload,
                    FirstFailedAt = DateTime.UtcNow,
                    LastAttemptAt = DateTime.UtcNow,
                    LastError = "Circuit OPEN"
                });
                _logger.LogWarning("sale.completed: WMS circuit OPEN, routed to DLQ. eventId={EventId}", ev.EventId);
            }

            health.MarkProcessed();
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "sale.completed: handler exception, nack-no-requeue. Body={Body}", body);
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
