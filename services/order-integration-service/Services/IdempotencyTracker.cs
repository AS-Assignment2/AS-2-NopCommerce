using System.Collections.Concurrent;

namespace OrderIntegrationService.Services;

/// <summary>
/// In-memory eventId deduplication. Lost on restart — sufficient for demo.
/// </summary>
public class IdempotencyTracker
{
    private readonly ConcurrentDictionary<string, DateTime> _seen = new();

    public bool TryMark(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return true;
        return _seen.TryAdd(eventId, DateTime.UtcNow);
    }

    public int Count => _seen.Count;
}
