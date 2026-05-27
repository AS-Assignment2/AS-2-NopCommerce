using Nop.Core.Domain.Orders;
using Nop.Services.Catalog;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Services.Integration;

public partial class StockUpdateHandler : IStockUpdateHandler
{
    private static readonly HashSet<string> _processedEventIds = new();
    private static readonly object _processedLock = new();

    private readonly IProductService _productService;
    private readonly IOrderService _orderService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOutboxService _outboxService;
    private readonly ILogger _logger;

    public StockUpdateHandler(
        IProductService productService,
        IOrderService orderService,
        IOrderProcessingService orderProcessingService,
        IOutboxService outboxService,
        ILogger logger)
    {
        _productService = productService;
        _orderService = orderService;
        _orderProcessingService = orderProcessingService;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task HandleAsync(StockUpdatedPayload payload)
    {
        if (!string.IsNullOrWhiteSpace(payload.EventId))
        {
            lock (_processedLock)
            {
                if (!_processedEventIds.Add(payload.EventId))
                {
                    return;
                }
            }
        }

        var product = await _productService.GetProductByIdAsync(payload.ProductId);
        if (product == null)
        {
            await _logger.WarningAsync($"StockUpdateHandler: product {payload.ProductId} not found, dropping event {payload.EventId}");
            return;
        }

        var currentStock = product.StockQuantity;
        var delta = payload.NewStockLevel - currentStock;

        if (delta != 0)
        {
            await _productService.AdjustInventoryAsync(product, delta,
                message: $"stock.updated from {payload.Source ?? "WMS"} (event {payload.EventId})");
        }

        await _logger.InformationAsync(
            $"StockUpdateHandler: product {payload.ProductId} {currentStock} → {payload.NewStockLevel} (source={payload.Source}, event={payload.EventId})");

        if (payload.NewStockLevel < 0)
        {
            await ResolveCrossChannelConflictAsync(payload);
        }
    }

    private async Task ResolveCrossChannelConflictAsync(StockUpdatedPayload payload)
    {
        var deficit = -payload.NewStockLevel;

        var since = payload.Timestamp == default
            ? DateTime.UtcNow.AddSeconds(-60)
            : payload.Timestamp.AddSeconds(-60);

        var recentOrders = await _orderService.SearchOrdersAsync(
            productId: payload.ProductId,
            createdFromUtc: since,
            osIds: new List<int> { (int)OrderStatus.Pending, (int)OrderStatus.Processing });

        var cancellable = recentOrders
            .OrderByDescending(o => o.CreatedOnUtc)
            .ToList();

        await _logger.WarningAsync(
            $"StockUpdateHandler: negative stock for product {payload.ProductId} (deficit={deficit}); evaluating {cancellable.Count} recent orders for cancellation");

        foreach (var order in cancellable)
        {
            if (deficit <= 0)
                break;

            var items = await _orderService.GetOrderItemsAsync(order.Id);
            var qty = items.Where(i => i.ProductId == payload.ProductId).Sum(i => i.Quantity);

            if (qty <= 0)
                continue;

            try
            {
                await _orderProcessingService.CancelOrderAsync(order, notifyCustomer: true);
                deficit -= qty;

                await _outboxService.WriteEventAsync("order.cancelled", new
                {
                    eventId = Guid.NewGuid().ToString(),
                    orderId = order.Id,
                    orderGuid = order.OrderGuid,
                    customerId = order.CustomerId,
                    reason = "cross-channel-stock-conflict",
                    productId = payload.ProductId,
                    triggeredByEvent = payload.EventId,
                    cancelledOnUtc = DateTime.UtcNow
                });

                await _logger.InformationAsync(
                    $"StockUpdateHandler: cancelled order {order.Id} to recover {qty} units of product {payload.ProductId} (deficit remaining={deficit})");
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync(
                    $"StockUpdateHandler: failed to cancel order {order.Id}: {ex.Message}", ex);
            }
        }

        if (deficit > 0)
        {
            await _logger.WarningAsync(
                $"StockUpdateHandler: could not fully resolve stock conflict for product {payload.ProductId}; remaining deficit={deficit}");
        }
    }
}
