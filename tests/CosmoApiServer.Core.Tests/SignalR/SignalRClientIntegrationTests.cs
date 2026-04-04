using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Runtime.CompilerServices;
using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace CosmoApiServer.Core.Tests.SignalR;

public class SignalRClientIntegrationTests
{
    [Fact]
    public async Task SignalR_AspNetClient_CanConnect_Invoke_And_UseOthersInGroup()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn1 = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .Build();

            await using var conn2 = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .Build();

            var conn1Received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var conn2Received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            conn1.On<string>("receive", message => conn1Received.TrySetResult(message));
            conn2.On<string>("receive", message => conn2Received.TrySetResult(message));

            await conn1.StartAsync(cts.Token);
            await conn2.StartAsync(cts.Token);

            var echoed = await conn1.InvokeAsync<string>("Echo", "hello", cts.Token);
            Assert.Equal("hello", echoed);

            await conn1.InvokeAsync("Join", "room1", cts.Token);
            await conn2.InvokeAsync("Join", "room1", cts.Token);

            await conn1.InvokeAsync("SendToOthers", "room1", "group-message", cts.Token);

            var winner = await Task.WhenAny(conn2Received.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.Same(conn2Received.Task, winner);
            Assert.Equal("group-message", await conn2Received.Task);
            Assert.False(conn1Received.Task.IsCompleted, "Caller should not receive OthersInGroup messages.");

            await conn1.StopAsync(cts.Token);
            await conn2.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_MessagePack_CanConnect_And_Invoke()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Debug))
                .AddMessagePackProtocol()
                .Build();

            await conn.StartAsync(cts.Token);

            var echoed = await conn.InvokeAsync<string>("Echo", "messagepack", cts.Token);
            Assert.Equal("messagepack", echoed);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_WebSocket_InvalidHandshake_ClosesConnection()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            using var http = new HttpClient();
            var negotiateJson = await http.GetStringAsync($"http://localhost:{port}/chat/negotiate", cts.Token);
            var token = ExtractConnectionToken(negotiateJson);
            Assert.False(string.IsNullOrWhiteSpace(token));

            using var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("json");
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}/chat?id={token}"), cts.Token);

            var badHandshake = Encoding.UTF8.GetBytes("{\"protocol\":\"unsupported\",\"version\":1}\u001e");
            await ws.SendAsync(badHandshake, WebSocketMessageType.Text, true, cts.Token);

            var buffer = new byte[256];
            var result = await ws.ReceiveAsync(buffer, cts.Token);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            var payload = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Assert.Contains("error", payload, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_CanInvoke_MultiArgumentMethod()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .Build();

            await conn.StartAsync(cts.Token);

            var combined = await conn.InvokeAsync<string>("Combine", "left", 42, cts.Token);
            Assert.Equal("left:42", combined);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_IHubContext_CanBroadcast_ToConnectedClient()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .Build();

            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            conn.On<string>("receive", message => received.TrySetResult(message));

            await conn.StartAsync(cts.Token);

            var registry = app.Services.GetRequiredService<HubContextRegistry>();
            var hubContext = registry.Get<InteropHub>();
            Assert.NotNull(hubContext);

            await hubContext!.Clients.All.SendAsync("Receive", "server-broadcast", cts.Token);

            var winner = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.Same(received.Task, winner);
            Assert.Equal("server-broadcast", await received.Task);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_IHubContext_CanSend_ToSpecificClient()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn1 = new HubConnectionBuilder().WithUrl($"http://localhost:{port}/chat").Build();
            await using var conn2 = new HubConnectionBuilder().WithUrl($"http://localhost:{port}/chat").Build();

            var received1 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var received2 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            conn1.On<string>("receive", message => received1.TrySetResult(message));
            conn2.On<string>("receive", message => received2.TrySetResult(message));

            await conn1.StartAsync(cts.Token);
            await conn2.StartAsync(cts.Token);

            var registry = app.Services.GetRequiredService<HubContextRegistry>();
            var hubContext = registry.Get<InteropHub>();
            Assert.NotNull(hubContext);
            Assert.False(string.IsNullOrWhiteSpace(conn2.ConnectionId));

            await hubContext!.Clients.Client(conn2.ConnectionId!).SendAsync("Receive", "only-conn2", cts.Token);

            var winner = await Task.WhenAny(received2.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.Same(received2.Task, winner);
            Assert.Equal("only-conn2", await received2.Task);
            Assert.False(received1.Task.IsCompleted, "Non-targeted client should not receive direct send.");

            await conn1.StopAsync(cts.Token);
            await conn2.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_IHubContext_CanSend_ToGroupOnly()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn1 = new HubConnectionBuilder().WithUrl($"http://localhost:{port}/chat").Build();
            await using var conn2 = new HubConnectionBuilder().WithUrl($"http://localhost:{port}/chat").Build();

            var received1 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var received2 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            conn1.On<string>("receive", message => received1.TrySetResult(message));
            conn2.On<string>("receive", message => received2.TrySetResult(message));

            await conn1.StartAsync(cts.Token);
            await conn2.StartAsync(cts.Token);

            await conn1.InvokeAsync("Join", "vip", cts.Token);

            var registry = app.Services.GetRequiredService<HubContextRegistry>();
            var hubContext = registry.Get<InteropHub>();
            Assert.NotNull(hubContext);

            await hubContext!.Clients.Group("vip").SendAsync("Receive", "group-only", cts.Token);

            var winner = await Task.WhenAny(received1.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.Same(received1.Task, winner);
            Assert.Equal("group-only", await received1.Task);
            Assert.False(received2.Task.IsCompleted, "Non-group client should not receive group send.");

            await conn1.StopAsync(cts.Token);
            await conn2.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_SendAsync_OneWayInvocation_Works()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .Build();

            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            conn.On<string>("receive", message => received.TrySetResult(message));

            await conn.StartAsync(cts.Token);
            await conn.SendAsync("SendToCaller", "one-way", cts.Token);

            var winner = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.Same(received.Task, winner);
            Assert.Equal("one-way", await received.Task);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_CanStop_AndRestart_SameConnection()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .Build();

            await conn.StartAsync(cts.Token);
            var firstConnectionId = conn.ConnectionId;
            Assert.False(string.IsNullOrWhiteSpace(firstConnectionId));

            await conn.StopAsync(cts.Token);

            await conn.StartAsync(cts.Token);
            var secondConnectionId = conn.ConnectionId;
            Assert.False(string.IsNullOrWhiteSpace(secondConnectionId));
            Assert.NotEqual(firstConnectionId, secondConnectionId);

            var echoed = await conn.InvokeAsync<string>("Echo", "after-restart", cts.Token);
            Assert.Equal("after-restart", echoed);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_Hub_OnDisconnectedAsync_IsCalled_WhenClientStops()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var tracker = new DisconnectTracker();
        tracker.Reset();

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();
        builder.Services.AddSingleton(tracker);

        var app = builder.Build();
        app.MapHub<DisconnectTrackingHub>("/disconnect");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/disconnect")
                .Build();

            await conn.StartAsync(cts.Token);
            var connectionId = conn.ConnectionId;
            Assert.False(string.IsNullOrWhiteSpace(connectionId));

            await conn.StopAsync(cts.Token);

            var winner = await Task.WhenAny(tracker.Disconnected.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.Same(tracker.Disconnected.Task, winner);

            var disconnected = await tracker.Disconnected.Task;
            Assert.Equal(connectionId, disconnected.ConnectionId);
            Assert.Null(disconnected.ExceptionMessage);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_InvokeAsync_SurfacesHubErrors()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .Build();

            await conn.StartAsync(cts.Token);

            var ex = await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => conn.InvokeAsync("Fail", cts.Token));
            Assert.Contains("boom", ex.Message, StringComparison.OrdinalIgnoreCase);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_CanReceive_ServerStream()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .Build();

            await conn.StartAsync(cts.Token);

            var items = new List<int>();
            await foreach (var item in conn.StreamAsync<int>("CountTo", 4, cts.Token))
                items.Add(item);

            Assert.Equal([1, 2, 3, 4], items);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_StreamAsync_SurfacesStreamFailure()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .Build();

            await conn.StartAsync(cts.Token);

            var items = new List<int>();
            var ex = await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(async () =>
            {
                await foreach (var item in conn.StreamAsync<int>("CountThenFail", cts.Token))
                    items.Add(item);
            });

            Assert.Equal([1, 2], items);
            Assert.Contains("stream-boom", ex.Message, StringComparison.OrdinalIgnoreCase);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_WithAutomaticReconnect_CanStart_AndInvoke()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();

        var app = builder.Build();
        app.MapHub<InteropHub>("/chat");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .WithAutomaticReconnect()
                .Build();

            await conn.StartAsync(cts.Token);
            var echoed = await conn.InvokeAsync<string>("Echo", "reconnect-enabled", cts.Token);
            Assert.Equal("reconnect-enabled", echoed);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_WithAutomaticReconnect_Reconnects_AfterServerRestart()
    {
        int port = GetFreeTcpPort();
        using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        static CosmoWebApplication CreateApp(int port)
        {
            var builder = CosmoWebApplicationBuilder.Create();
            builder.ListenOn(port);
            builder.AddSignalR();

            var app = builder.Build();
            app.MapHub<InteropHub>("/chat");
            return app;
        }

        var app1 = CreateApp(port);
        using var app1Cts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
        var runTask1 = Task.Run(() => app1.RunAsync(app1Cts.Token));

        try
        {
            await WaitForServerAsync(port, overallCts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/chat")
                .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500)])
                .Build();

            var reconnecting = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reconnected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            conn.Reconnecting += ex =>
            {
                reconnecting.TrySetResult(ex);
                return Task.CompletedTask;
            };
            conn.Reconnected += connectionId =>
            {
                reconnected.TrySetResult(connectionId);
                return Task.CompletedTask;
            };

            await conn.StartAsync(overallCts.Token);
            var before = await conn.InvokeAsync<string>("Echo", "before-restart", overallCts.Token);
            Assert.Equal("before-restart", before);

            app1Cts.Cancel();
            try { await runTask1; } catch (OperationCanceledException) { }

            var reconnectingWinner = await Task.WhenAny(reconnecting.Task, Task.Delay(TimeSpan.FromSeconds(5), overallCts.Token));
            Assert.Same(reconnecting.Task, reconnectingWinner);

            var app2 = CreateApp(port);
            using var app2Cts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
            var runTask2 = Task.Run(() => app2.RunAsync(app2Cts.Token));

            try
            {
                await WaitForServerAsync(port, overallCts.Token);

                var reconnectedWinner = await Task.WhenAny(reconnected.Task, Task.Delay(TimeSpan.FromSeconds(10), overallCts.Token));
                Assert.Same(reconnected.Task, reconnectedWinner);
                Assert.False(string.IsNullOrWhiteSpace(await reconnected.Task));

                var after = await conn.InvokeAsync<string>("Echo", "after-restart", overallCts.Token);
                Assert.Equal("after-restart", after);
            }
            finally
            {
                app2Cts.Cancel();
                try { await runTask2; } catch (OperationCanceledException) { }
            }
        }
        finally
        {
            try { app1Cts.Cancel(); } catch { }
            try { await runTask1; } catch (OperationCanceledException) { } catch { }
        }
    }

    [Fact]
    public async Task SignalR_WebSocket_StreamCancelFrame_CancelsServerInvocation()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var tracker = new StreamCancellationTracker();
        tracker.Reset();

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();
        builder.Services.AddSingleton(tracker);

        var app = builder.Build();
        app.MapHub<StreamingHub>("/streaming");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            using var http = new HttpClient();
            var negotiateJson = await http.GetStringAsync($"http://localhost:{port}/streaming/negotiate", cts.Token);
            var token = ExtractConnectionToken(negotiateJson);
            Assert.False(string.IsNullOrWhiteSpace(token));

            using var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("json");
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}/streaming?id={token}"), cts.Token);

            await SendWebSocketTextAsync(ws, "{\"protocol\":\"json\",\"version\":1}\u001e", cts.Token);
            await ReceiveSignalRFrameAsync(ws, cts.Token); // handshake ack

            const string invocationId = "stream-1";
            await SendWebSocketTextAsync(ws,
                "{\"type\":4,\"invocationId\":\"stream-1\",\"target\":\"CountUntilCanceled\",\"arguments\":[]}\u001e",
                cts.Token);

            var items = new List<int>();
            while (items.Count < 3)
            {
                var frame = await ReceiveSignalRFrameAsync(ws, cts.Token);
                using var doc = JsonDocument.Parse(frame);
                if (doc.RootElement.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetInt32() == 2 &&
                    doc.RootElement.TryGetProperty("invocationId", out var invProp) &&
                    invProp.GetString() == invocationId)
                {
                    items.Add(doc.RootElement.GetProperty("item").GetInt32());
                }
            }

            Assert.Equal([1, 2, 3], items);

            await SendWebSocketTextAsync(ws,
                "{\"type\":5,\"invocationId\":\"stream-1\"}\u001e",
                cts.Token);

            var winner = await Task.WhenAny(tracker.Canceled.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.Same(tracker.Canceled.Task, winner);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task SignalR_AspNetClient_StreamAsChannelAsync_CancellationToken_CancelsServerInvocation()
    {
        int port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var tracker = new StreamCancellationTracker();
        tracker.Reset();

        var builder = CosmoWebApplicationBuilder.Create();
        builder.ListenOn(port);
        builder.AddSignalR();
        builder.Services.AddSingleton(tracker);

        var app = builder.Build();
        app.MapHub<StreamingHub>("/streaming");

        var runTask = Task.Run(() => app.RunAsync(cts.Token));

        try
        {
            await WaitForServerAsync(port, cts.Token);

            await using var conn = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{port}/streaming")
                .Build();

            await conn.StartAsync(cts.Token);

            using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var reader = await conn.StreamAsChannelAsync<int>("CountUntilCanceled", streamCts.Token);

            var items = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                Assert.True(await reader.WaitToReadAsync(cts.Token));
                Assert.True(reader.TryRead(out var item));
                items.Add(item);
            }

            Assert.Equal([1, 2, 3], items);
            streamCts.Cancel();

            var winner = await Task.WhenAny(tracker.Canceled.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.Same(tracker.Canceled.Task, winner);

            await conn.StopAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    private static async Task WaitForServerAsync(int port, CancellationToken ct)
    {
        using var client = new HttpClient();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync($"http://localhost:{port}/__ready", ct);
                return;
            }
            catch
            {
                await Task.Delay(100, ct);
            }
        }

        throw new TimeoutException("Server did not start in time.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string? ExtractConnectionToken(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("connectionToken", out var token) ? token.GetString() : null;
    }

    private static Task SendWebSocketTextAsync(ClientWebSocket ws, string payload, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, ct);

    private static async Task<string> ReceiveSignalRFrameAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, ct);
        return Encoding.UTF8.GetString(buffer, 0, result.Count)
            .Split('\u001e', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private sealed class InteropHub : Hub
    {
        public Task<string> Echo(string message) => Task.FromResult(message);

        public Task<string> Combine(string left, int right) => Task.FromResult($"{left}:{right}");

        public Task Join(string group) => HubGroups.AddToGroupAsync(HubContext.ConnectionId, group);

        public Task SendToOthers(string group, string message)
            => HubClients.OthersInGroup(group).SendAsync("Receive", message);

        public Task SendToCaller(string message)
            => HubClients.Caller.SendAsync("Receive", message);

        public Task Fail() => throw new InvalidOperationException("boom");

        public async IAsyncEnumerable<int> CountTo(int count)
        {
            for (var i = 1; i <= count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }

        public async IAsyncEnumerable<int> CountThenFail()
        {
            yield return 1;
            await Task.Yield();
            yield return 2;
            await Task.Yield();
            throw new InvalidOperationException("stream-boom");
        }
    }

    private sealed class StreamingHub(StreamCancellationTracker tracker) : Hub
    {
        public async IAsyncEnumerable<int> CountUntilCanceled([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var i = 0;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(10, cancellationToken);
                    yield return ++i;
                }
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                    tracker.Canceled.TrySetResult(true);
            }
        }
    }

    private sealed class DisconnectTrackingHub(DisconnectTracker tracker) : Hub
    {
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            // On Windows, a client-initiated disconnect surfaces as a write error rather than a
            // clean null; normalise write-related transport errors to null so tests are portable.
            if (exception?.Message?.Contains("Unable to write data to the transport connection") == true
                || exception is System.IO.IOException { InnerException: System.Net.Sockets.SocketException })
            {
                exception = null;
            }

            tracker.Disconnected.TrySetResult(new DisconnectRecord(HubContext.ConnectionId, exception?.Message));
            return Task.CompletedTask;
        }
    }

    private sealed class DisconnectTracker
    {
        public TaskCompletionSource<DisconnectRecord> Disconnected { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Reset() =>
            Disconnected = new TaskCompletionSource<DisconnectRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record DisconnectRecord(string ConnectionId, string? ExceptionMessage);

    private sealed class StreamCancellationTracker
    {
        public TaskCompletionSource<bool> Canceled { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Reset() =>
            Canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}
