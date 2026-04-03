using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.SignalR;

/// <summary>
/// Handles the full lifecycle of a SignalR hub connection:
/// WebSocket upgrade → handshake → message loop → disconnect.
/// </summary>
public sealed class HubDispatcher<THub> where THub : Hub
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HubConnectionManager _manager;
    private readonly Dictionary<string, MethodInfo> _methods;

    public HubDispatcher(HubConnectionManager manager)
    {
        _manager = manager;
        _methods = typeof(THub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToDictionary(m => m.Name, m => m, StringComparer.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(HttpContext httpContext)
    {
        // Negotiate endpoint — return available transports
        if (httpContext.Request.Path.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase))
        {
            var connId = Guid.NewGuid().ToString("N");
            httpContext.Response.Headers["Content-Type"] = "application/json";
            httpContext.Response.WriteJson(new
            {
                connectionId = connId,
                availableTransports = new[]
                {
                    new { transport = "WebSockets", transferFormats = new[] { "Text" } }
                }
            });
            return;
        }

        if (!httpContext.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = 400;
            httpContext.Response.WriteText("SignalR requires WebSocket transport.");
            return;
        }

        var cosmoSocket = await httpContext.AcceptWebSocketAsync();
        var connectionId = Guid.NewGuid().ToString("N");
        var hubConn = new HubConnection(connectionId, cosmoSocket);
        _manager.Add(connectionId, hubConn);

        // SignalR handshake: client sends {"protocol":"json","version":1}\u001e
        await PerformHandshakeAsync(cosmoSocket, httpContext.RequestAborted);

        var hub = ActivatorUtilities.CreateInstance<THub>(httpContext.RequestServices);
        hub.Context = new HubCallerContext(connectionId, httpContext.User, httpContext);
        hub.Clients = new HubCallerClientsAdapter(connectionId, _manager);
        hub.Groups = _manager;

        await hub.OnConnectedAsync();

        Exception? disconnectEx = null;
        try
        {
            var buffer = new byte[64 * 1024];
            var sb = new StringBuilder();

            while (true)
            {
                WebSocketReceiveResult result;
                try { result = await cosmoSocket.ReceiveAsync(buffer.AsMemory(), httpContext.RequestAborted); }
                catch (OperationCanceledException) { break; }

                if (result.MessageType == WebSocketMessageType.Close) break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var raw = sb.ToString();
                sb.Clear();

                foreach (var frame in raw.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                    await DispatchFrameAsync(hub, hubConn, frame, httpContext.RequestAborted);
            }
        }
        catch (Exception ex)
        {
            disconnectEx = ex;
        }
        finally
        {
            _manager.Remove(connectionId);
            await hub.OnDisconnectedAsync(disconnectEx);
            await hub.DisposeAsync();
            try { await cosmoSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed"); } catch { }
        }
    }

    private static async Task PerformHandshakeAsync(CosmoWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[256];
        await ws.ReceiveAsync(buf.AsMemory(), ct);
        var response = Encoding.UTF8.GetBytes("{}\u001e");
        await ws.SendAsync(response.AsMemory(), WebSocketMessageType.Text, true);
    }

    private async Task DispatchFrameAsync(THub hub, HubConnection conn, string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("type", out var typeProp)) return;

        switch (typeProp.GetInt32())
        {
            case 1:
                await HandleInvocationAsync(hub, doc.RootElement, conn, ct);
                break;
            case 6: // Ping → Pong
                await conn.SendAsync(Encoding.UTF8.GetBytes("{\"type\":6}\u001e"), ct);
                break;
        }
    }

    private async Task HandleInvocationAsync(THub hub, JsonElement msg, HubConnection conn, CancellationToken ct)
    {
        var target = msg.TryGetProperty("target", out var t) ? t.GetString() : null;
        if (target is null || !_methods.TryGetValue(target, out var method)) return;

        var invocationId = msg.TryGetProperty("invocationId", out var inv) ? inv.GetString() : null;
        var argsEl = msg.TryGetProperty("arguments", out var a) ? a : default;

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (argsEl.ValueKind == JsonValueKind.Array && i < argsEl.GetArrayLength())
                args[i] = argsEl[i].Deserialize(parameters[i].ParameterType, JsonOptions);
        }

        try
        {
            var returnVal = method.Invoke(hub, args);
            if (returnVal is Task task) await task;

            if (invocationId is not null)
            {
                object? result = null;
                if (returnVal is Task t2)
                    result = t2.GetType().GetProperty("Result")?.GetValue(t2);

                var completion = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new { type = 3, invocationId, result }) + "\u001e");
                await conn.SendAsync(completion, ct);
            }
        }
        catch (Exception ex)
        {
            if (invocationId is not null)
            {
                var error = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new { type = 3, invocationId, error = ex.InnerException?.Message ?? ex.Message }) + "\u001e");
                await conn.SendAsync(error, ct);
            }
        }
    }

    private sealed class HubCallerClientsAdapter(string callerId, HubConnectionManager manager) : IHubCallerClients
    {
        public IClientProxy Caller => manager.Client(callerId);
        public IClientProxy Others => manager.AllExcept([callerId]);
        public IClientProxy OthersInGroup(string g) => manager.Group(g); // caller filtering left to caller

        public IClientProxy All => manager.All;
        public IClientProxy Client(string id) => manager.Client(id);
        public IClientProxy Clients(IEnumerable<string> ids) => manager.Clients(ids);
        public IClientProxy Group(string g) => manager.Group(g);
        public IClientProxy Groups(IEnumerable<string> gs) => manager.Groups(gs);
        public IClientProxy AllExcept(IEnumerable<string> ex) => manager.AllExcept(ex);
    }
}
