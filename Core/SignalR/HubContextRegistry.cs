using System.Collections.Concurrent;

namespace CosmoApiServer.Core.SignalR;

/// <summary>
/// Singleton registry that holds <see cref="IHubContext{THub}"/> instances,
/// populated by <c>app.MapHub&lt;THub&gt;(path)</c>.
/// Resolve via <c>services.GetRequiredService&lt;IHubContext&lt;THub&gt;&gt;()</c>
/// after the application is started, or inject <see cref="HubContextRegistry"/>
/// and call <see cref="Get{THub}"/>.
/// </summary>
public sealed class HubContextRegistry
{
    private readonly ConcurrentDictionary<Type, object> _contexts = new();

    internal void Register<THub>(HubConnectionManager manager) where THub : Hub =>
        _contexts[typeof(THub)] = new HubContext<THub>(manager);

    public IHubContext<THub>? Get<THub>() where THub : Hub =>
        _contexts.TryGetValue(typeof(THub), out var ctx) ? (IHubContext<THub>)ctx : null;
}
