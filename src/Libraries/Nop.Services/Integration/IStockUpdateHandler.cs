namespace Nop.Services.Integration;

/// <summary>
/// Applies a stock.updated event from the WMS to the nopCommerce catalog and
/// resolves cross-channel conflicts (negative stock from concurrent POS sales).
/// </summary>
public partial interface IStockUpdateHandler
{
    Task HandleAsync(StockUpdatedPayload payload);
}
