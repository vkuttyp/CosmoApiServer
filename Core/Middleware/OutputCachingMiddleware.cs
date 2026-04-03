using System.Collections.Concurrent;
using System.Text;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

// ── Options ───────────────────────────────────────────────────────────────────

public sealed class OutputCacheOptions
{
    /// <summary>Default cache duration when no policy specifies otherwise.</summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromSeconds(60);
}

public sealed class OutputCachePolicy
{
    public TimeSpan? Expiry { get; set; }
    public IReadOnlyList<string> VaryByHeaders { get; init; } = [];
    public IReadOnlyList<string> VaryByQueryKeys { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];

    public static OutputCachePolicy Default { get; } = new();

    public static OutputCachePolicyBuilder Build() => new();
}

public sealed class OutputCachePolicyBuilder
{
    private TimeSpan? _expiry;
    private readonly List<string> _headers = [];
    private readonly List<string> _queryKeys = [];
    private readonly List<string> _tags = [];

    public OutputCachePolicyBuilder Expire(TimeSpan expiry) { _expiry = expiry; return this; }
    public OutputCachePolicyBuilder VaryByHeader(params string[] headers) { _headers.AddRange(headers); return this; }
    public OutputCachePolicyBuilder VaryByQuery(params string[] keys) { _queryKeys.AddRange(keys); return this; }
    public OutputCachePolicyBuilder Tag(params string[] tags) { _tags.AddRange(tags); return this; }

    public OutputCachePolicy ToPolicy() => new()
    {
        Expiry = _expiry,
        VaryByHeaders = _headers,
        VaryByQueryKeys = _queryKeys,
        Tags = _tags
    };
}

// ── Cache store ───────────────────────────────────────────────────────────────

public interface IOutputCacheStore
{
    Task<OutputCacheEntry?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, OutputCacheEntry entry, IReadOnlyList<string> tags, TimeSpan expiry, CancellationToken ct);
    Task EvictByTagAsync(string tag, CancellationToken ct);
}

public sealed class OutputCacheEntry(int statusCode, Dictionary<string, string> headers, byte[] body, DateTimeOffset createdAt, TimeSpan expiry)
{
    public int StatusCode => statusCode;
    public Dictionary<string, string> Headers => headers;
    public byte[] Body => body;
    public bool IsExpired => DateTimeOffset.UtcNow > createdAt + expiry;
}

public sealed class InMemoryOutputCacheStore : IOutputCacheStore
{
    private readonly ConcurrentDictionary<string, OutputCacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _tagIndex = new();

    public Task<OutputCacheEntry?> GetAsync(string key, CancellationToken ct)
    {
        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            return Task.FromResult<OutputCacheEntry?>(entry);
        _cache.TryRemove(key, out _);
        return Task.FromResult<OutputCacheEntry?>(null);
    }

    public Task SetAsync(string key, OutputCacheEntry entry, IReadOnlyList<string> tags, TimeSpan expiry, CancellationToken ct)
    {
        _cache[key] = entry;
        foreach (var tag in tags)
        {
            var set = _tagIndex.GetOrAdd(tag, _ => []);
            lock (set) { set.Add(key); }
        }
        return Task.CompletedTask;
    }

    public Task EvictByTagAsync(string tag, CancellationToken ct)
    {
        if (!_tagIndex.TryGetValue(tag, out var keys)) return Task.CompletedTask;
        string[] snapshot;
        lock (keys) { snapshot = [.. keys]; }
        foreach (var key in snapshot) _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

// ── Attribute ─────────────────────────────────────────────────────────────────

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class OutputCacheAttribute : Attribute
{
    public int DurationSeconds { get; set; } = 60;
    public string[] Tags { get; set; } = [];
    public string[] VaryByHeaders { get; set; } = [];
    public string[] VaryByQueryKeys { get; set; } = [];
}

// ── Middleware ────────────────────────────────────────────────────────────────

public sealed class OutputCachingMiddleware(IOutputCacheStore store, OutputCacheOptions options) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Only cache GET/HEAD; skip if client sends Cache-Control: no-cache
        if (context.Request.Method is not (Http.HttpMethod.GET or Http.HttpMethod.HEAD))
        {
            await next(context);
            return;
        }

        if (context.Request.Headers.TryGetValue("Cache-Control", out var cc) &&
            cc.Contains("no-cache", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var policy = context.Items.TryGetValue("__OutputCachePolicy", out var p)
            ? (OutputCachePolicy)p! : OutputCachePolicy.Default;

        var key = BuildKey(context, policy);
        var cached = await store.GetAsync(key, context.RequestAborted);

        if (cached is not null)
        {
            context.Response.StatusCode = cached.StatusCode;
            foreach (var (k, v) in cached.Headers)
                context.Response.Headers[k] = v;
            context.Response.Headers["X-Output-Cache"] = "HIT";
            context.Response.Write(cached.Body);
            return;
        }

        // Capture body bytes written during next()
        var capture = new List<byte>();
        context.Response.BodyCapture = capture;

        await next(context);

        context.Response.BodyCapture = null;

        // Only cache successful, non-empty responses
        if (context.Response.StatusCode == 200 && capture.Count > 0)
        {
            var expiry = policy.Expiry ?? options.DefaultExpiry;
            var headers = new Dictionary<string, string>(context.Response.Headers, StringComparer.OrdinalIgnoreCase);
            headers.Remove("X-Output-Cache");

            var entry = new OutputCacheEntry(
                context.Response.StatusCode,
                headers,
                [.. capture],
                DateTimeOffset.UtcNow,
                expiry);

            await store.SetAsync(key, entry, policy.Tags, expiry, context.RequestAborted);
            context.Response.Headers["X-Output-Cache"] = "MISS";
        }
    }

    private static string BuildKey(HttpContext ctx, OutputCachePolicy policy)
    {
        var sb = new StringBuilder(ctx.Request.Method.ToString()).Append(':').Append(ctx.Request.Path);

        if (policy.VaryByQueryKeys.Count > 0)
        {
            foreach (var key in policy.VaryByQueryKeys)
            {
                var val = GetQueryParam(ctx.Request.QueryString, key);
                sb.Append('|').Append(key).Append('=').Append(val);
            }
        }
        else
        {
            sb.Append(ctx.Request.QueryString);
        }

        foreach (var header in policy.VaryByHeaders)
        {
            if (ctx.Request.Headers.TryGetValue(header, out var v))
                sb.Append('|').Append(header).Append(':').Append(v);
        }

        return sb.ToString();
    }

    private static string? GetQueryParam(string qs, string key)
    {
        if (string.IsNullOrEmpty(qs)) return null;
        var span = qs.AsSpan().TrimStart('?').ToString();
        foreach (var part in span.Split('&'))
        {
            var idx = part.IndexOf('=');
            if (idx > 0 && part[..idx].Equals(key, StringComparison.OrdinalIgnoreCase))
                return part[(idx + 1)..];
        }
        return null;
    }
}

// ── Extensions ────────────────────────────────────────────────────────────────

public static class OutputCacheExtensions
{
    /// <summary>Sets the output cache policy for this request (from filters or middleware).</summary>
    public static void SetOutputCachePolicy(this HttpContext context, OutputCachePolicy policy)
        => context.Items["__OutputCachePolicy"] = policy;

    /// <summary>Evict all cached entries tagged with the given tag.</summary>
    public static Task EvictByTagAsync(this IOutputCacheStore store, string tag, CancellationToken ct = default)
        => store.EvictByTagAsync(tag, ct);
}
