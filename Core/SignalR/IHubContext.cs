namespace CosmoApiServer.Core.SignalR;

/// <summary>
/// Allows server-side code outside of a hub (e.g., background services, controllers)
/// to send messages to hub clients. Inject IHubContext&lt;THub&gt; from DI.
/// </summary>
public interface IHubContext<THub> where THub : Hub
{
    IHubClients Clients { get; }
    IGroupManager Groups { get; }
}

internal sealed class HubContext<THub>(HubConnectionManager manager) : IHubContext<THub>
    where THub : Hub
{
    public IHubClients Clients => manager;
    public IGroupManager Groups => manager;
}
