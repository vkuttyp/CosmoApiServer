using System.Security.Claims;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.SignalR;

/// <summary>
/// Base class for SignalR-compatible hubs.
/// </summary>
public abstract class Hub : IAsyncDisposable
{
    internal HubCallerContext Context { get; set; } = null!;
    internal IHubCallerClients Clients { get; set; } = null!;
    internal IGroupManager Groups { get; set; } = null!;

    /// <summary>Context for the current caller (connection ID, user, etc.).</summary>
    public HubCallerContext HubContext => Context;

    /// <summary>Proxy to send messages to clients.</summary>
    public IHubCallerClients HubClients => Clients;

    /// <summary>Group management (add/remove connections from named groups).</summary>
    public IGroupManager HubGroups => Groups;

    /// <summary>Called when a client connects. Override to run on-connect logic.</summary>
    public virtual Task OnConnectedAsync() => Task.CompletedTask;

    /// <summary>Called when a client disconnects. Override to run on-disconnect logic.</summary>
    public virtual Task OnDisconnectedAsync(Exception? exception) => Task.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class HubCallerContext(string connectionId, ClaimsPrincipal? user, HttpContext httpContext)
{
    public string ConnectionId => connectionId;
    public ClaimsPrincipal? User => user;
    public HttpContext HttpContext => httpContext;
    public Dictionary<object, object?> Items { get; } = new();
    public CancellationToken ConnectionAborted => httpContext.RequestAborted;
}
