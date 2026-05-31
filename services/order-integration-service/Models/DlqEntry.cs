namespace OrderIntegrationService.Models;

public class DlqEntry
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty; // "WMS" or "ERP"
    public string Payload { get; set; } = string.Empty; // serialized JSON
    public DateTime FirstFailedAt { get; set; }
    public DateTime LastAttemptAt { get; set; }
    public int RetryCount { get; set; }
    public string LastError { get; set; } = string.Empty;
}
