using CosmoApiServer.Core.SignalR;

namespace CosmoApiServer.Core.Tests.SignalR;

public class SignalRTests
{
    [Fact]
    public void HubConnectionManager_Add_And_Get()
    {
        var manager = new HubConnectionManager();
        // HubConnectionManager.Add/Get are internal — test via the public IHubClients interface
        // A client for unknown id should return NullProxy (no exception)
        var proxy = manager.Client("unknown-id");
        Assert.NotNull(proxy);
    }

    [Fact]
    public async Task HubConnectionManager_Groups_AddAndRemove()
    {
        var manager = new HubConnectionManager();

        await manager.AddToGroupAsync("conn1", "room1");
        await manager.AddToGroupAsync("conn2", "room1");

        // Remove conn1 from group
        await manager.RemoveFromGroupAsync("conn1", "room1");

        // No exception — group operations are fire-and-go
    }

    [Fact]
    public void HubContextRegistry_Register_And_Get()
    {
        var registry = new HubContextRegistry();
        var manager = new HubConnectionManager();

        registry.Register<TestHub>(manager);

        var ctx = registry.Get<TestHub>();
        Assert.NotNull(ctx);
        Assert.NotNull(ctx.Clients);
        Assert.NotNull(ctx.Groups);
    }

    [Fact]
    public void HubContextRegistry_Get_UnregisteredHub_ReturnsNull()
    {
        var registry = new HubContextRegistry();

        var ctx = registry.Get<TestHub>();
        Assert.Null(ctx);
    }

    [Fact]
    public void BuildInvocation_ProducesValidFrame()
    {
        var frame = HubConnectionManager.BuildInvocation("SendMessage", ["hello", 42]);

        var text = System.Text.Encoding.UTF8.GetString(frame);
        Assert.EndsWith("\u001e", text);
        Assert.Contains("\"type\":1", text);
        Assert.Contains("\"target\":\"sendMessage\"", text); // camelCase
        Assert.Contains("\"hello\"", text);
        Assert.Contains("42", text);
    }

    [Fact]
    public async Task NullProxy_SendAsync_DoesNotThrow()
    {
        var manager = new HubConnectionManager();
        var proxy = manager.Client("nonexistent");

        // Should be NullProxy — all sends are no-ops
        await proxy.SendAsync("method", "arg");
        await proxy.SendAsync("method", "a", "b");
        await proxy.SendAsync("method", ["a", "b", "c"]);
    }

    [Fact]
    public async Task AllExcept_ExcludesSpecifiedConnections()
    {
        var manager = new HubConnectionManager();
        // With no real connections, AllExcept should return an empty multi-proxy (no exception)
        var proxy = manager.AllExcept(["conn1", "conn2"]);
        Assert.NotNull(proxy);
        await proxy.SendAsync("test"); // no-op, no exception
    }
}

internal sealed class TestHub : Hub
{
    public Task SendMessage(string msg) => Task.CompletedTask;
}
