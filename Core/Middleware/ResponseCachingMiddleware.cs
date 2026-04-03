using System.Security.Cryptography;
using System.Text;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class ResponseCachingOptions
{
    /// <summary>Maximum response body size to cache (bytes). Default 1 MB.</summary>
    public int MaxBodySize { get; set; } = 1 * 1024 * 1024;

    /// <summary>Cache-Control max-age to use when the response doesn't set one. Zero means not set.</summary>
    public int DefaultMaxAgeSeconds { get; set; } = 0;
}

/// <summary>
/// In-memory response cache keyed by method + path + query.
/// Only caches GET/HEAD 200 responses that include Cache-Control: max-age or public.
/// </summary>
public sealed class ResponseCachingMiddleware(ResponseCachingOptions options) : IMiddleware
{
    private readonly Dictionary<string, CachedResponse> _store = new();
    private readonly Lock _lock = new();

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Only cache GET and HEAD
        if (context.Request.Method != CosmoApiServer.Core.Http.HttpMethod.GET &&
            context.Request.Method != CosmoApiServer.Core.Http.HttpMethod.HEAD)
        {
            await next(context);
            return;
        }

        var key = BuildKey(context.Request);

        // Check cache
        lock (_lock)
        {
            if (_store.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                ServeCached(context, cached);
                return;
            }
            _store.Remove(key);
        }

        // Capture the response
        var originalBody = context.Response.Body;
        await next(context);

        // Only cache 200 responses within size limit
        if (context.Response.StatusCode != 200 || !context.Response.IsBuffered) return;
        var body = context.Response.Body;
        if (body.Length > options.MaxBodySize) return;

        var maxAge = ParseMaxAge(context.Response.Headers);
        if (maxAge <= 0) maxAge = options.DefaultMaxAgeSeconds;
        if (maxAge <= 0) return;

        var entry = new CachedResponse
        {
            StatusCode = context.Response.StatusCode,
            Headers = context.Response.Headers.ToDictionary(k => k.Key, v => v.Value),
            Body = body,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(maxAge),
            ETag = ComputeETag(body)
        };

        lock (_lock) { _store[key] = entry; }
    }

    private static void ServeCached(HttpContext context, CachedResponse cached)
    {
        // ETag / If-None-Match support
        if (context.Request.Headers.TryGetValue("If-None-Match", out var inm) && inm == cached.ETag)
        {
            context.Response.StatusCode = 304;
            return;
        }

        context.Response.StatusCode = cached.StatusCode;
        foreach (var (name, value) in cached.Headers)
            context.Response.Headers[name] = value;
        context.Response.Headers["X-Cache"] = "HIT";
        context.Response.Headers["ETag"] = cached.ETag;

        if (context.Request.Method != CosmoApiServer.Core.Http.HttpMethod.HEAD)
            context.Response.Write(cached.Body);
    }

    private static int ParseMaxAge(Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Cache-Control", out var cc)) return 0;
        foreach (var part in cc.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(trimmed["max-age=".Length..], out var seconds))
                return seconds;
        }
        return 0;
    }

    private static string ComputeETag(byte[] body)
    {
        var hash = SHA256.HashData(body);
        return $"\"{Convert.ToHexString(hash)[..16]}\"";
    }

    private static string BuildKey(HttpRequest req)
    {
        var sb = new StringBuilder(req.Path);
        if (!string.IsNullOrEmpty(req.QueryString)) sb.Append('?').Append(req.QueryString);
        return sb.ToString();
    }

    private sealed class CachedResponse
    {
        public required int StatusCode { get; init; }
        public required Dictionary<string, string> Headers { get; init; }
        public required byte[] Body { get; init; }
        public required DateTimeOffset ExpiresAt { get; set; }
        public required string ETag { get; init; }
    }
}
