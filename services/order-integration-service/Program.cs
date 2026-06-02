using System.Net;
using System.Text.Json;
using OrderIntegrationService.Configuration;
using OrderIntegrationService.Models;
using OrderIntegrationService.Services;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration objects ---
var rabbitConfig = builder.Configuration.GetSection("RabbitMq").Get<RabbitMqConfig>() ?? new RabbitMqConfig();
var erpConfig = builder.Configuration.GetSection("Erp").Get<ErpConfig>() ?? new ErpConfig();
var wmsConfig = builder.Configuration.GetSection("Wms").Get<WmsConfig>() ?? new WmsConfig();
var reconConfig = builder.Configuration.GetSection("Reconciliation").Get<ReconciliationConfig>() ?? new ReconciliationConfig();

// Env overrides (so docker-compose can set host/port/url without bind mounts)
rabbitConfig.Host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? rabbitConfig.Host;
if (int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var rmqPort)) rabbitConfig.Port = rmqPort;
rabbitConfig.Username = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? rabbitConfig.Username;
rabbitConfig.Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? rabbitConfig.Password;
rabbitConfig.Exchange = Environment.GetEnvironmentVariable("RABBITMQ_EXCHANGE") ?? rabbitConfig.Exchange;
erpConfig.BaseUrl = Environment.GetEnvironmentVariable("ERP_BASE_URL") ?? erpConfig.BaseUrl;
wmsConfig.BaseUrl = Environment.GetEnvironmentVariable("WMS_BASE_URL") ?? wmsConfig.BaseUrl;

builder.Services.AddSingleton(rabbitConfig);
builder.Services.AddSingleton(erpConfig);
builder.Services.AddSingleton(wmsConfig);
builder.Services.AddSingleton(reconConfig);

// --- Shared state (singletons) ---
// HealthState is built up-front so the circuit-breaker policy below can close
// over the same instance the rest of the app uses.
var healthState = new HealthState();
builder.Services.AddSingleton(healthState);
builder.Services.AddSingleton<IdempotencyTracker>();
builder.Services.AddSingleton<DeadLetterQueue>();
builder.Services.AddSingleton<RabbitMqPublisher>();

// Circuit breaker policy is built ONCE so its state survives across requests.
// (AddPolicyHandler with a factory would create a fresh breaker per request.)
var wmsBreaker = HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 3,
        durationOfBreak: TimeSpan.FromSeconds(30),
        onBreak: (_, _) => healthState.CircuitState = "OPEN",
        onReset: () => healthState.CircuitState = "CLOSED",
        onHalfOpen: () => healthState.CircuitState = "HALF_OPEN");

// --- HTTP clients with Polly policies ---
// ERP: retry 3x exponential backoff (1s, 2s, 4s) on 5xx + transient errors.
builder.Services.AddHttpClient<ErpAdapter>(client =>
{
    client.BaseAddress = new Uri(erpConfig.BaseUrl);
    client.Timeout = erpConfig.Timeout;
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry - 1))));

// WMS: circuit breaker — 3 consecutive failures → OPEN for 30s.
builder.Services.AddHttpClient<WmsAdapter>(client =>
{
    client.BaseAddress = new Uri(wmsConfig.BaseUrl);
    client.Timeout = wmsConfig.Timeout;
})
.AddPolicyHandler(wmsBreaker);

// AddHttpClient<TClient> already registers ErpAdapter and WmsAdapter (transient).
builder.Services.AddHostedService<OrderPlacedConsumer>();
builder.Services.AddHostedService<SaleCompletedConsumer>();
builder.Services.AddHostedService<ReconciliationService>();

// CORS so the browser dashboard can call /health and the demo buttons.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();

// --- /health ---
app.MapGet("/health", (HealthState state, DeadLetterQueue dlq) =>
{
    var depth = dlq.Depth;
    var status = state.CircuitState switch
    {
        "OPEN" => "degraded",
        "HALF_OPEN" => "recovering",
        _ => depth > 0 ? "degraded" : "healthy"
    };
    var payload = new IntegrationHealth
    {
        Status = status,
        CircuitBreakerState = state.CircuitState,
        DlqDepth = depth,
        LastProcessedAt = state.LastProcessedAt?.ToString("o"),
        EventsProcessed = state.EventsProcessed,
        DuplicatesSkipped = state.DuplicatesSkipped
    };
    var http = status == "degraded" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;
    return Results.Json(payload, statusCode: http);
});

// --- /dlq (snapshot, for demo and inspection) ---
app.MapGet("/dlq", (DeadLetterQueue dlq) => Results.Ok(new
{
    depth = dlq.Depth,
    entries = dlq.Snapshot()
}));

app.MapPost("/dlq/clear", (DeadLetterQueue dlq) =>
{
    var depth = dlq.Depth;
    dlq.Clear();
    return Results.Ok(new { cleared = depth });
});

// --- /webhooks/stock-changed: replaces wms-event-adapter once IS is online ---
app.MapPost("/webhooks/stock-changed", async (StockChangedWebhook payload, RabbitMqPublisher publisher, RabbitMqConfig cfg, ILogger<Program> logger) =>
{
    if (payload is null || payload.ProductId <= 0)
        return Results.BadRequest(new { error = "invalid payload" });

    var body = JsonSerializer.Serialize(new
    {
        eventId = string.IsNullOrEmpty(payload.EventId) ? Guid.NewGuid().ToString() : payload.EventId,
        productId = payload.ProductId,
        warehouseId = payload.WarehouseId,
        newStockQuantity = payload.NewStockQuantity,
        occurredAt = string.IsNullOrEmpty(payload.OccurredAt) ? DateTime.UtcNow.ToString("o") : payload.OccurredAt
    });

    try
    {
        await publisher.PublishAsync(cfg.StockUpdatedRoutingKey, body);
        return Results.Ok(new { published = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to publish stock.updated");
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
});

app.Run();
