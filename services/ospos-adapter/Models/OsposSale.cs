namespace OsposAdapter.Models;

public class OsposSale
{
    public int SaleId { get; set; }
    public DateTime SaleTime { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}
