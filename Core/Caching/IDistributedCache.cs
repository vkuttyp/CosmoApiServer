namespace CosmoApiServer.Core.Caching;

/// <summary>
/// Distributed cache abstraction. The default implementation is an in-process memory store.
/// Swap for Redis, SQL, etc. by registering a different IDistributedCache in DI.
/// </summary>
public interface IDistributedCache
{
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions? options = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RefreshAsync(string key, CancellationToken ct = default);
}

public sealed class DistributedCacheEntryOptions
{
    public DateTimeOffset? AbsoluteExpiration { get; set; }
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
    public TimeSpan? SlidingExpiration { get; set; }
}

public static class DistributedCacheExtensions
{
    public static async Task<string?> GetStringAsync(this IDistributedCache cache, string key, CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync(key, ct);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    public static Task SetStringAsync(this IDistributedCache cache, string key, string value,
        DistributedCacheEntryOptions? options = null, CancellationToken ct = default)
        => cache.SetAsync(key, System.Text.Encoding.UTF8.GetBytes(value), options, ct);
}

/// <summary>In-process default implementation. Replace with Redis etc. via DI.</summary>
public sealed class InMemoryDistributedCache : IDistributedCache
{
    private sealed class Entry(byte[] value, DateTimeOffset? absoluteExpiry, TimeSpan? sliding)
    {
        public byte[] Value = value;
        public DateTimeOffset? AbsoluteExpiry = absoluteExpiry;
        public TimeSpan? SlidingExpiration = sliding;
        public DateTimeOffset LastAccess = DateTimeOffset.UtcNow;

        public bool IsExpired(DateTimeOffset now)
        {
            if (AbsoluteExpiry.HasValue && now >= AbsoluteExpiry.Value) return true;
            if (SlidingExpiration.HasValue && now - LastAccess > SlidingExpiration.Value) return true;
            return false;
        }
    }

    private readonly Dictionary<string, Entry> _store = new();
    private readonly Lock _lock = new();

    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_store.TryGetValue(key, out var e)) return Task.FromResult<byte[]?>(null);
            if (e.IsExpired(DateTimeOffset.UtcNow)) { _store.Remove(key); return Task.FromResult<byte[]?>(null); }
            e.LastAccess = DateTimeOffset.UtcNow;
            return Task.FromResult<byte[]?>(e.Value);
        }
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions? options = null, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? absExpiry = options?.AbsoluteExpiration
            ?? (options?.AbsoluteExpirationRelativeToNow.HasValue == true ? now + options.AbsoluteExpirationRelativeToNow : null);
        lock (_lock) { _store[key] = new Entry(value, absExpiry, options?.SlidingExpiration); }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        lock (_lock) { _store.Remove(key); }
        return Task.CompletedTask;
    }

    public Task RefreshAsync(string key, CancellationToken ct = default)
    {
        lock (_lock) { if (_store.TryGetValue(key, out var e)) e.LastAccess = DateTimeOffset.UtcNow; }
        return Task.CompletedTask;
    }
}
