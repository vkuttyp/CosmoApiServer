using System.Collections.Concurrent;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

/// <summary>
/// Options for the Rate Limiting middleware.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Maximum number of requests allowed per window.</summary>
    public int Limit { get; set; } = 100;

    /// <summary>The time window for the limit.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The status code to return when the limit is exceeded. Default is 429 Too Many Requests.</summary>
    public int StatusCode { get; set; } = 429;

    /// <summary>Message returned in the response body when rate limited.</summary>
    public string Message { get; set; } = "Rate limit exceeded. Try again later.";
}

/// <summary>
/// High-performance Fixed-Window Rate Limiting middleware.
/// Tracks requests per IP address using a concurrent dictionary with atomic updates.
/// </summary>
public sealed class RateLimitingMiddleware(RateLimitOptions options) : IMiddleware
{
    private readonly ConcurrentDictionary<string, RateLimitEntry> _cache = new();
    private long _lastCleanup = Environment.TickCount64;

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var ip = GetClientIp(context);
        var now = DateTime.UtcNow;

        var entry = _cache.GetOrAdd(ip, _ => new RateLimitEntry(now.Add(options.Window)));

        // Atomic increment and window check
        var windowEnd = entry.WindowEnd;
        if (now > windowEnd)
        {
            // Window expired — reset atomically
            entry.Reset(now.Add(options.Window));
        }

        var count = entry.Increment();

        if (count > options.Limit)
        {
            // Limit exceeded
            context.Response.StatusCode = options.StatusCode;
            context.Response.Headers["Retry-After"] = ((int)(entry.WindowEnd - now).TotalSeconds).ToString();
            context.Response.WriteJson(new { error = "RateLimitExceeded", message = options.Message });
            return;
        }

        await next(context);

        // Periodic cleanup of expired entries (every 60 seconds)
        var tickNow = Environment.TickCount64;
        var lastCleanup = Interlocked.Read(ref _lastCleanup);
        if (tickNow - lastCleanup > 60_000 &&
            Interlocked.CompareExchange(ref _lastCleanup, tickNow, lastCleanup) == lastCleanup)
        {
            foreach (var kvp in _cache)
            {
                if (now > kvp.Value.WindowEnd)
                    _cache.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static string GetClientIp(HttpContext context)
    {
        // Check X-Forwarded-For first (for reverse proxy setups)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xff) && !string.IsNullOrEmpty(xff))
        {
            // Take the first (leftmost) IP which is the original client
            var commaIdx = xff.IndexOf(',');
            return commaIdx > 0 ? xff[..commaIdx].Trim() : xff.Trim();
        }

        // Fall back to remote endpoint from context items (set by transport)
        if (context.Items.TryGetValue("__RemoteIP", out var remoteIp) && remoteIp is string ip)
            return ip;

        return "unknown";
    }

    /// <summary>Thread-safe rate limit entry using Interlocked operations.</summary>
    private sealed class RateLimitEntry(DateTime windowEnd)
    {
        private int _count;
        public DateTime WindowEnd { get; private set; } = windowEnd;

        public int Increment() => Interlocked.Increment(ref _count);

        public void Reset(DateTime newWindowEnd)
        {
            WindowEnd = newWindowEnd;
            Interlocked.Exchange(ref _count, 0);
        }
    }
}
