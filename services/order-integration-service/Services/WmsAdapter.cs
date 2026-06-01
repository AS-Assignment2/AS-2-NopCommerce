using System.Text.Json;
using OrderIntegrationService.Models;
using Polly.CircuitBreaker;

namespace OrderIntegrationService.Services;

/// <summary>
/// Forwards order/sale events to the WMS stub. Circuit breaker policy is
/// configured on the HttpClient (Polly via AddPolicyHandler in Program.cs) —
/// 3 consecutive failures → OPEN for 30s. While OPEN, calls throw immediately
/// (BrokenCircuitException) and the caller pushes the entry to DLQ.
/// </summary>
public class WmsAdapter
{
    private readonly HttpClient _http;
    private readonly ILogger<WmsAdapter> _logger;

    public WmsAdapter(HttpClient http, ILogger<WmsAdapter> logger)
    {
        _http = http;
        _logger = logger;
    }

    public static string BuildReservationPayload(OrderPlacedEvent ev) => JsonSerializer.Serialize(new
    {
        orderId = ev.OrderId,
        items = ev.Items.Select(i => new { productId = i.ProductId, quantity = i.Quantity }).ToList()
    });

    public static string BuildSalePayload(SaleCompletedEvent ev)
    {
        var syntheticOrderId = Math.Abs(ev.EventId.GetHashCode());
        return JsonSerializer.Serialize(new
        {
            orderId = syntheticOrderId,
            items = ev.Items.Select(i => new { productId = SkuToProductId(i.Sku), quantity = (int)i.Quantity }).ToList()
        });
    }

    public Task<(bool ok, string? error)> ReserveOrderAsync(OrderPlacedEvent ev, CancellationToken ct)
        => PostRawAsync("/reservations", BuildReservationPayload(ev), ct);

    public Task<(bool ok, string? error)> ReserveSaleAsync(SaleCompletedEvent ev, CancellationToken ct)
        => PostRawAsync("/reservations", BuildSalePayload(ev), ct);

    public async Task<(bool ok, string? error)> PostRawAsync(string path, string jsonBody, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(path, content, ct);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("WMS {Path} OK", path);
                return (true, null);
            }
            var msg = $"HTTP {(int)resp.StatusCode}";
            _logger.LogWarning("WMS {Path} failed {Msg}", path, msg);
            return (false, msg);
        }
        catch (BrokenCircuitException)
        {
            // Let the caller react (mark health, route to DLQ).
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMS {Path} threw", path);
            return (false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static int SkuToProductId(string sku)
    {
        if (sku.StartsWith("P", StringComparison.OrdinalIgnoreCase) && int.TryParse(sku[1..], out var id))
            return id;
        return Math.Abs(sku.GetHashCode()) % 100000;
    }
}
