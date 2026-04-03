namespace CosmoApiServer.Core.SignalR;

/// <summary>Proxy for sending messages to connected clients.</summary>
public interface IHubClients
{
    IClientProxy All { get; }
    IClientProxy Client(string connectionId);
    IClientProxy Clients(IEnumerable<string> connectionIds);
    IClientProxy Group(string groupName);
    IClientProxy Groups(IEnumerable<string> groupNames);
    IClientProxy AllExcept(IEnumerable<string> excludedConnectionIds);
}

/// <summary>Extends IHubClients with caller-specific proxies.</summary>
public interface IHubCallerClients : IHubClients
{
    IClientProxy Caller { get; }
    IClientProxy Others { get; }
    IClientProxy OthersInGroup(string groupName);
}

/// <summary>Sends a named invocation message to one or more clients.</summary>
public interface IClientProxy
{
    Task SendAsync(string method, object? arg1 = null, CancellationToken ct = default);
    Task SendAsync(string method, object? arg1, object? arg2, CancellationToken ct = default);
    Task SendAsync(string method, object?[] args, CancellationToken ct = default);
}

/// <summary>Group management for adding/removing connections.</summary>
public interface IGroupManager
{
    Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default);
    Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default);
}
