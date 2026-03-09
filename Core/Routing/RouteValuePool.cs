using System.Collections.Concurrent;

namespace CosmoApiServer.Core.Routing;

/// <summary>
/// A lightweight, thread-safe pool for route value dictionaries to reduce GC pressure.
/// </summary>
internal static class RouteValuePool
{
    private static readonly ConcurrentQueue<Dictionary<string, string>> _pool = new();
    private const int MaxPoolSize = 1024;

    /// <summary>
    /// Borrows a dictionary from the pool or creates a new one.
    /// </summary>
    public static Dictionary<string, string> Rent()
    {
        if (_pool.TryDequeue(out var dict))
        {
            return dict;
        }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a dictionary to the pool after clearing it.
    /// </summary>
    public static void Return(Dictionary<string, string>? dict)
    {
        if (dict == null || _pool.Count >= MaxPoolSize) return;
        dict.Clear();
        _pool.Enqueue(dict);
    }
}
