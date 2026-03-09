using System.Collections.Concurrent;

namespace CosmoApiServer.Core.Http;

/// <summary>
/// A lightweight, thread-safe pool for HttpContext objects to reduce GC pressure.
/// </summary>
internal static class HttpContextPool
{
    private static readonly ConcurrentQueue<HttpContext> _pool = new();
    private const int MaxPoolSize = 1024;

    /// <summary>
    /// Borrows an HttpContext from the pool or creates a new one.
    /// </summary>
    public static HttpContext Rent()
    {
        if (_pool.TryDequeue(out var context))
        {
            return context;
        }
        return new HttpContext();
    }

    /// <summary>
    /// Returns an HttpContext to the pool after resetting it.
    /// </summary>
    public static void Return(HttpContext? context)
    {
        if (context == null || _pool.Count >= MaxPoolSize) return;
        context.Reset();
        _pool.Enqueue(context);
    }
}
