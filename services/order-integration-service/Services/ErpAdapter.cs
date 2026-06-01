using System.Text.Json;
using OrderIntegrationService.Models;

namespace OrderIntegrationService.Services;

/// <summary>
/// Forwards order/sale events to the ERP stub. Retry policy is configured on
/// the HttpClient (Polly via AddPolicyHandler in Program.cs) — 3 retries with
/// exponential backoff. On final failure, the caller pushes the entry to DLQ.
/// </summary>
public class ErpAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<ErpAdapter> _logger;

    public ErpAdapter(HttpClient http, ILogger<ErpAdapter> logger)
    {
        _http = http;
        _logger = logger;
    }

    public static string BuildOrderPayload(OrderPlacedEvent ev) => JsonSerializer.Serialize(new
    {
        eventId = ev.EventId,
        orderId = ev.OrderId,
        customerId = ev.CustomerId,
        items = ev.Items.Select(i => new
        {
            productId = i.ProductId,
            sku = $"P{i.ProductId}",
            quantity = i.Quantity
        }).ToList(),
        totalAmount = (double)ev.Total,
        occurredAt = ev.CreatedOnUtc.ToString("o")
    });

    public static string BuildSalePayload(SaleCompletedEvent ev)
    {
        var syntheticOrderId = Math.Abs(ev.EventId.GetHashCode());
        return JsonSerializer.Serialize(new
        {
            eventId = ev.EventId,
            orderId = syntheticOrderId,
            customerId = 0,
            items = ev.Items.Select(i => new
            {
                productId = 0,
                sku = i.Sku,
                quantity = (int)i.Quantity
            }).ToList(),
            totalAmount = 0.0,
            occurredAt = ev.Timestamp.ToString("o")
        });
    }

    public Task<bool> ForwardOrderAsync(OrderPlacedEvent ev, CancellationToken ct)
        => PostRawAsync("/orders", BuildOrderPayload(ev), ct);

    public Task<bool> ForwardSaleAsync(SaleCompletedEvent ev, CancellationToken ct)
        => PostRawAsync("/orders", BuildSalePayload(ev), ct);

    public async Task<bool> PostRawAsync(string path, string jsonBody, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(path, content, ct);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("ERP {Path} OK", path);
                return true;
            }
            _logger.LogWarning("ERP {Path} failed status={Status}", path, (int)resp.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ERP {Path} threw", path);
            return false;
        }
    }
}
