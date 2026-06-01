namespace OrderIntegrationService.Configuration;

public class ErpConfig
{
    public string BaseUrl { get; set; } = "http://erp-stub:8001";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

public class WmsConfig
{
    public string BaseUrl { get; set; } = "http://wms-stub:8002";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}

public class ReconciliationConfig
{
    public int IntervalSeconds { get; set; } = 5;
}
