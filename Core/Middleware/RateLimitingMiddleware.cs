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

    /// <summary>
    /// Set to true when the server sits behind a trusted reverse proxy (e.g. nginx) that
    /// appends the real client IP to X-Forwarded-For. When true, the rightmost XFF value
    /// is used (added by the trusted proxy) instead of the leftmost (client-controlled).
    /// Default false — uses the socket remote IP from __RemoteIP.
    /// </summary>
    public bool TrustProxy { get; set; } = false;
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

        // Atomically check expiry and reset within the entry to avoid TOCTOU race
        var count = entry.IncrementWithWindowReset(now, options.Window);

        if (count > options.Limit)
        {
            // Limit exceeded
            context.Response.StatusCode = options.StatusCode;
            context.Response.Headers["Retry-After"] = Math.Max(1, (int)(entry.WindowEnd - now).TotalSeconds).ToString();
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

    private string GetClientIp(HttpContext context)
    {
        // Always prefer the real socket IP when available
        if (context.Items.TryGetValue("__RemoteIP", out var remoteIp) && remoteIp is string socketIp)
        {
            if (!options.TrustProxy)
                return socketIp;

            // Behind a trusted proxy: use X-Forwarded-For but take the rightmost value,
            // which is appended by the trusted proxy (leftmost values are client-controlled).
            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xff) && !string.IsNullOrEmpty(xff))
            {
                var commaIdx = xff.LastIndexOf(',');
                return commaIdx >= 0 ? xff[(commaIdx + 1)..].Trim() : xff.Trim();
            }

            return socketIp;
        }

        return "unknown";
    }

    /// <summary>Thread-safe rate limit entry using Interlocked operations.</summary>
    private sealed class RateLimitEntry(DateTime windowEnd)
    {
        private int _count;
        // Store ticks as long so WindowEnd reads/writes are atomic via Interlocked
        private long _windowEndTicks = windowEnd.Ticks;
        private readonly Lock _resetLock = new();

        public DateTime WindowEnd => new(Interlocked.Read(ref _windowEndTicks), DateTimeKind.Utc);

        public int Increment() => Interlocked.Increment(ref _count);

        /// <summary>
        /// Atomically checks if the window has expired, resets if necessary, then increments.
        /// Using a per-entry lock makes the check+reset+increment sequence race-free.
        /// </summary>
        public int IncrementWithWindowReset(DateTime now, TimeSpan window)
        {
            lock (_resetLock)
            {
                var windowEndTicks = Interlocked.Read(ref _windowEndTicks);
                if (now.Ticks > windowEndTicks)
                {
                    // Window expired — reset count and advance window
                    Interlocked.Exchange(ref _count, 0);
                    Interlocked.Exchange(ref _windowEndTicks, now.Add(window).Ticks);
                }
                return Interlocked.Increment(ref _count);
            }
        }

        public void Reset(DateTime newWindowEnd)
        {
            Interlocked.Exchange(ref _count, 0);
            Interlocked.Exchange(ref _windowEndTicks, newWindowEnd.Ticks);
        }
    }
}
