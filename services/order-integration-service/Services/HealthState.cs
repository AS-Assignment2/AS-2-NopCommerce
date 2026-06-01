namespace OrderIntegrationService.Services;

/// <summary>
/// Singleton holding circuit state, last-processed timestamp, and counters
/// surfaced via /health for the dashboard.
/// </summary>
public class HealthState
{
    private long _eventsProcessed;
    private long _duplicatesSkipped;
    private DateTime? _lastProcessedAt;
    private string _circuitState = "CLOSED";
    private readonly object _lock = new();

    public string CircuitState
    {
        get { lock (_lock) return _circuitState; }
        set { lock (_lock) _circuitState = value; }
    }

    public DateTime? LastProcessedAt
    {
        get { lock (_lock) return _lastProcessedAt; }
    }

    public long EventsProcessed => Interlocked.Read(ref _eventsProcessed);
    public long DuplicatesSkipped => Interlocked.Read(ref _duplicatesSkipped);

    public void MarkProcessed()
    {
        Interlocked.Increment(ref _eventsProcessed);
        lock (_lock) _lastProcessedAt = DateTime.UtcNow;
    }

    public void MarkDuplicate()
    {
        Interlocked.Increment(ref _duplicatesSkipped);
    }
}
