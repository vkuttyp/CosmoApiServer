using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class SessionOptions
{
    public string CookieName { get; set; } = ".Cosmo.Session";
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(20);
    public bool HttpOnly { get; set; } = true;
    public bool SecureOnly { get; set; } = false;
    public string SameSite { get; set; } = "Lax";
}

public sealed class SessionMiddleware(SessionOptions options) : IMiddleware
{
    // In-memory session store: sessionId → (data, lastAccessed)
    private readonly Dictionary<string, (Dictionary<string, byte[]> Data, DateTimeOffset LastAccess)> _store = new();
    private readonly Lock _lock = new();
    private DateTimeOffset _lastCleanup = DateTimeOffset.UtcNow;

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sessionId = GetOrCreateSessionId(context);
        var session = new InMemorySession(sessionId, LoadSession(sessionId), ct =>
        {
            SaveSession(sessionId, ((InMemorySession)context.Session!).GetData());
            return Task.CompletedTask;
        });
        context.Session = session;

        // Refresh session cookie on each request
        SetCookie(context, sessionId);

        CleanupExpiredSessions();

        await next(context);

        // Commit session changes
        if (session.IsModified)
            SaveSession(sessionId, session.GetData());
    }

    private string GetOrCreateSessionId(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(options.CookieName, out var existing) && !string.IsNullOrEmpty(existing))
            return existing;
        return Guid.NewGuid().ToString("N");
    }

    private Dictionary<string, byte[]> LoadSession(string sessionId)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(sessionId, out var entry))
            {
                _store[sessionId] = (entry.Data, DateTimeOffset.UtcNow);
                return new Dictionary<string, byte[]>(entry.Data);
            }
            return new Dictionary<string, byte[]>();
        }
    }

    private void SaveSession(string sessionId, Dictionary<string, byte[]> data)
    {
        lock (_lock) { _store[sessionId] = (data, DateTimeOffset.UtcNow); }
    }

    private void SetCookie(HttpContext context, string sessionId)
    {
        var cookie = $"{options.CookieName}={sessionId}; Path=/; SameSite={options.SameSite}";
        if (options.HttpOnly) cookie += "; HttpOnly";
        if (options.SecureOnly) cookie += "; Secure";
        context.Response.Headers["Set-Cookie"] = cookie;
    }

    private void CleanupExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastCleanup < TimeSpan.FromMinutes(5)) return;
        _lastCleanup = now;
        lock (_lock)
        {
            var expired = _store.Where(kv => now - kv.Value.LastAccess > options.IdleTimeout)
                                .Select(kv => kv.Key).ToList();
            foreach (var id in expired) _store.Remove(id);
        }
    }

    private sealed class InMemorySession(string id, Dictionary<string, byte[]> data, Func<CancellationToken, Task> commit) : ISession
    {
        private readonly Dictionary<string, byte[]> _data = data;
        public bool IsModified { get; private set; }

        public string Id => id;
        public bool IsAvailable => true;
        public IEnumerable<string> Keys => _data.Keys;

        public void Set(string key, byte[] value) { _data[key] = value; IsModified = true; }
        public bool TryGetValue(string key, out byte[] value) => _data.TryGetValue(key, out value!);
        public void Remove(string key) { _data.Remove(key); IsModified = true; }
        public void Clear() { _data.Clear(); IsModified = true; }
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct = default) => commit(ct);
        public Dictionary<string, byte[]> GetData() => _data;
    }
}
