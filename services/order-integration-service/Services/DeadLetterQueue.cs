using System.Collections.Concurrent;
using OrderIntegrationService.Models;

namespace OrderIntegrationService.Services;

/// <summary>
/// In-memory DLQ. Entries are popped by the reconciliation loop and either
/// re-processed (on success) or re-enqueued (on continued failure).
/// </summary>
public class DeadLetterQueue
{
    private readonly ConcurrentQueue<DlqEntry> _queue = new();

    public void Enqueue(DlqEntry entry) => _queue.Enqueue(entry);

    public bool TryDequeue(out DlqEntry entry)
    {
        if (_queue.TryDequeue(out var e))
        {
            entry = e!;
            return true;
        }
        entry = null!;
        return false;
    }

    public int Depth => _queue.Count;

    public IReadOnlyCollection<DlqEntry> Snapshot() => _queue.ToArray();

    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
    }
}
