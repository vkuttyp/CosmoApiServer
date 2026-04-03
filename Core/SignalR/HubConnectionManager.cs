using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.SignalR;

/// <summary>
/// Tracks all active hub connections and implements client/group proxies.
/// Registered as a singleton per hub type.
/// </summary>
public sealed class HubConnectionManager : IHubClients, IGroupManager
{
    private readonly ConcurrentDictionary<string, HubConnection> _connections = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── Connection lifecycle ──────────────────────────────────────────────────

    internal void Add(string connectionId, HubConnection conn) => _connections[connectionId] = conn;
    internal void Remove(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
        foreach (var group in _groups.Values)
            lock (group) { group.Remove(connectionId); }
    }
    internal HubConnection? Get(string connectionId) =>
        _connections.TryGetValue(connectionId, out var c) ? c : null;

    // ── IHubClients ───────────────────────────────────────────────────────────

    public IClientProxy All => new MultiProxy([.. _connections.Values]);
    public IClientProxy Client(string id) =>
        _connections.TryGetValue(id, out var c) ? new SingleProxy(c) : NullProxy.Instance;
    public IClientProxy Clients(IEnumerable<string> ids) =>
        new MultiProxy([.. ids.Select(id => _connections.TryGetValue(id, out var c) ? c : null).OfType<HubConnection>()]);
    public IClientProxy Group(string name) => new MultiProxy([.. GetGroupConnections(name)]);
    public IClientProxy Groups(IEnumerable<string> names) =>
        new MultiProxy([.. names.SelectMany(GetGroupConnections)]);
    public IClientProxy AllExcept(IEnumerable<string> excluded)
    {
        var set = new HashSet<string>(excluded);
        return new MultiProxy([.. _connections.Where(kv => !set.Contains(kv.Key)).Select(kv => kv.Value)]);
    }

    // ── IGroupManager ─────────────────────────────────────────────────────────

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        var group = _groups.GetOrAdd(groupName, _ => []);
        lock (group) { group.Add(connectionId); }
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        if (_groups.TryGetValue(groupName, out var group))
            lock (group) { group.Remove(connectionId); }
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerable<HubConnection> GetGroupConnections(string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var group)) return [];
        string[] ids;
        lock (group) { ids = [.. group]; }
        return ids.Select(id => _connections.TryGetValue(id, out var c) ? c : null).OfType<HubConnection>();
    }

    internal static byte[] BuildInvocation(string method, object?[] args)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = 1,
            target = JsonNamingPolicy.CamelCase.ConvertName(method),
            arguments = args
        }, JsonOptions);
        return Encoding.UTF8.GetBytes(msg + "\u001e");
    }

    // ── Proxy implementations ─────────────────────────────────────────────────

    private sealed class SingleProxy(HubConnection conn) : IClientProxy
    {
        public Task SendAsync(string m, object? a = null, CancellationToken ct = default)
            => conn.SendAsync(BuildInvocation(m, [a]), ct);
        public Task SendAsync(string m, object? a, object? b, CancellationToken ct = default)
            => conn.SendAsync(BuildInvocation(m, [a, b]), ct);
        public Task SendAsync(string m, object?[] args, CancellationToken ct = default)
            => conn.SendAsync(BuildInvocation(m, args), ct);
    }

    private sealed class MultiProxy(HubConnection[] conns) : IClientProxy
    {
        public Task SendAsync(string m, object? a = null, CancellationToken ct = default)
            => Task.WhenAll(conns.Select(c => c.SendAsync(BuildInvocation(m, [a]), ct)));
        public Task SendAsync(string m, object? a, object? b, CancellationToken ct = default)
            => Task.WhenAll(conns.Select(c => c.SendAsync(BuildInvocation(m, [a, b]), ct)));
        public Task SendAsync(string m, object?[] args, CancellationToken ct = default)
            => Task.WhenAll(conns.Select(c => c.SendAsync(BuildInvocation(m, args), ct)));
    }

    private sealed class NullProxy : IClientProxy
    {
        public static readonly NullProxy Instance = new();
        public Task SendAsync(string m, object? a = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync(string m, object? a, object? b, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync(string m, object?[] args, CancellationToken ct = default) => Task.CompletedTask;
    }
}

/// <summary>Wraps a CosmoWebSocket with a send lock to prevent concurrent writes.</summary>
public sealed class HubConnection(string connectionId, CosmoWebSocket socket)
{
    public string ConnectionId => connectionId;
    public CosmoWebSocket Socket => socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try { await socket.SendAsync(data.AsMemory(), WebSocketMessageType.Text, true); }
        finally { _sendLock.Release(); }
    }
}
