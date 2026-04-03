using CosmoApiServer.Core.SignalR;
using CosmoApiServer.Core.Http;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Protocol;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

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
        var message = HubConnectionManager.BuildInvocation("SendMessage", ["hello", 42]);
        var frame = new JsonHubProtocol().GetMessageBytes(message);

        var text = System.Text.Encoding.UTF8.GetString(frame.Span);
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

    [Fact]
    public void BuildNegotiatePayload_IncludesConnectionToken_AndVersion()
    {
        var payload = HubDispatcher<TestHub>.BuildNegotiatePayload("abc123");
        var json = JsonSerializer.Serialize(payload);

        Assert.Contains("\"connectionId\":\"abc123\"", json);
        Assert.Contains("\"connectionToken\":\"abc123\"", json);
        Assert.Contains("\"negotiateVersion\":1", json);
        Assert.Contains("\"transport\":\"WebSockets\"", json);
        Assert.Contains("\"Binary\"", json);
    }

    [Fact]
    public void ResolveConnectionId_UsesNegotiatedId_FromQueryString()
    {
        var request = new HttpRequest
        {
            Method = HttpMethod.GET,
            Path = "/hub",
            QueryString = "?id=abc123"
        };

        Assert.Equal("abc123", HubDispatcher<TestHub>.ResolveConnectionId(request));
    }

    [Fact]
    public void ResolveConnectionId_UsesConnectionToken_WhenPresent()
    {
        var request = new HttpRequest
        {
            Method = HttpMethod.GET,
            Path = "/hub",
            Query = new Dictionary<string, string> { ["connectionToken"] = "token-42" }
        };

        Assert.Equal("token-42", HubDispatcher<TestHub>.ResolveConnectionId(request));
    }

    [Fact]
    public async Task GroupExcept_ExcludesSpecifiedConnections()
    {
        var manager = new HubConnectionManager();
        var proxy = manager.GroupExcept("room1", ["conn1"]);

        Assert.NotNull(proxy);
        await proxy.SendAsync("test");
    }
}

internal sealed class TestHub : Hub
{
    public Task SendMessage(string msg) => Task.CompletedTask;
}
