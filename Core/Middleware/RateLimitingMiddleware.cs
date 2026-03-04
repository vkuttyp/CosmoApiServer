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
/// Tracks requests per IP address using a concurrent dictionary.
/// </summary>
public sealed class RateLimitingMiddleware(RateLimitOptions options) : IMiddleware
{
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowEnd)> _cache = new();

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Simple IP-based identification. 
        // In production, this might need to respect X-Forwarded-For headers.
        var ip = GetClientIp(context);
        var now = DateTime.UtcNow;

        var (count, windowEnd) = _cache.GetOrAdd(ip, _ => (0, now.Add(options.Window)));

        if (now > windowEnd)
        {
            // Window expired, reset
            _cache[ip] = (1, now.Add(options.Window));
        }
        else if (count >= options.Limit)
        {
            // Limit exceeded
            context.Response.StatusCode = options.StatusCode;
            context.Response.Headers["Retry-After"] = ((int)(windowEnd - now).TotalSeconds).ToString();
            context.Response.WriteJson(new { error = "RateLimitExceeded", message = options.Message });
            return;
        }
        else
        {
            // Increment count
            _cache[ip] = (count + 1, windowEnd);
        }

        await next(context);

        // Periodic cleanup would be needed in a long-running server to prevent memory leaks.
        // For now, we prioritize the hot-path performance.
    }

    private static string GetClientIp(HttpContext context)
    {
        // Placeholder: in a real implementation, we'd pull this from the socket or headers
        return "127.0.0.1"; 
    }
}
