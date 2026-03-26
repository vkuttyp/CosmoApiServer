using System.Collections.Concurrent;

namespace CosmoApiServer.Core.Http;

/// <summary>
/// A lightweight, thread-safe pool for HttpContext objects to reduce GC pressure.
/// </summary>
internal static class HttpContextPool
{
    private static readonly ConcurrentQueue<HttpContext> _pool = new();
    private static int _count;
    private const int MaxPoolSize = 1024;

    /// <summary>
    /// Borrows an HttpContext from the pool or creates a new one.
    /// </summary>
    public static HttpContext Rent()
    {
        if (_pool.TryDequeue(out var context))
        {
            Interlocked.Decrement(ref _count);
            return context;
        }
        return new HttpContext();
    }

    /// <summary>
    /// Returns an HttpContext to the pool after resetting it.
    /// </summary>
    public static void Return(HttpContext? context)
    {
        if (context is null) return;
        // Volatile.Read is a cheap ordered read — avoids the O(n) ConcurrentQueue.Count traversal
        if (Volatile.Read(ref _count) >= MaxPoolSize) return;
        context.Reset();
        _pool.Enqueue(context);
        Interlocked.Increment(ref _count);
    }
}
