namespace OnlineSeverGame.Server;

/// <summary>
/// Many producers (receive threads), one consumer (game loop). Producers append under a lock; the
/// consumer swaps the whole back buffer out once per tick so it holds the lock only for a pointer swap.
/// </summary>
public sealed class LockedQueue<T>
{
    private readonly object _lock = new();
    private List<T> _back = new();

    public void Enqueue(T item)
    {
        lock (_lock) _back.Add(item);
    }

    /// <summary>Moves everything queued so far into <paramref name="spare"/>'s place and returns it. The
    /// returned list belongs to the caller until it is passed back in as the next spare (cleared).</summary>
    public List<T> Swap(List<T> spare)
    {
        spare.Clear();
        lock (_lock)
        {
            var full = _back;
            _back = spare;
            return full;
        }
    }
}
