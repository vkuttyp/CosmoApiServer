using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Collections;
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
    private readonly Dictionary<string, string> _pendingConnections = new(StringComparer.Ordinal);

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
            lock (_pendingConnections)
                _pendingConnections[connId] = connId;

            httpContext.Response.Headers["Content-Type"] = "application/json";
            httpContext.Response.WriteJson(BuildNegotiatePayload(connId));
            return;
        }

        if (!httpContext.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = 400;
            httpContext.Response.WriteText("SignalR requires WebSocket transport.");
            return;
        }

        var connectionId = ResolveConnectionId(httpContext.Request);
        lock (_pendingConnections)
        {
            if (!string.IsNullOrWhiteSpace(connectionId) && _pendingConnections.Remove(connectionId))
            {
                // negotiated id reused as-is
            }
            else if (string.IsNullOrWhiteSpace(connectionId))
            {
                connectionId = Guid.NewGuid().ToString("N");
            }
        }

        httpContext.PrepareWebSocketUpgrade();
        httpContext.Items["__WebSocketHandler"] = (Func<Stream, Task>)(stream =>
            RunConnectionAsync(httpContext, stream, connectionId));
    }

    private async Task RunConnectionAsync(HttpContext httpContext, Stream stream, string connectionId)
    {
        using var cosmoSocket = new CosmoWebSocket(stream);
        if (!await PerformHandshakeAsync(cosmoSocket, CancellationToken.None))
            return;

        var hubConn = new HubConnection(connectionId, cosmoSocket);
        _manager.Add(connectionId, hubConn);

        var hub = ActivatorUtilities.CreateInstance<THub>(httpContext.RequestServices);
        hub.Context = new HubCallerContext(connectionId, httpContext.User, httpContext);
        hub.Clients = new HubCallerClientsAdapter(connectionId, _manager);
        hub.Groups = _manager;

        await hub.OnConnectedAsync();

        Exception? disconnectEx = null;
        var activeStreams = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        try
        {
            var buffer = new byte[64 * 1024];
            var sb = new StringBuilder();

            while (true)
            {
                WebSocketReceiveResult result;
                try { result = await cosmoSocket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None); }
                catch (OperationCanceledException) { break; }

                if (result.MessageType == WebSocketMessageType.Close) break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var raw = sb.ToString();
                sb.Clear();

                foreach (var frame in raw.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                    await DispatchFrameAsync(hub, hubConn, activeStreams, frame, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            disconnectEx = ex;
        }
        finally
        {
            foreach (var streamCts in activeStreams.Values)
                streamCts.Cancel();
            foreach (var streamCts in activeStreams.Values)
                streamCts.Dispose();

            _manager.Remove(connectionId);
            await hub.OnDisconnectedAsync(disconnectEx);
            await hub.DisposeAsync();
            try { await cosmoSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed"); } catch { }
        }
    }

    private static async Task<bool> PerformHandshakeAsync(CosmoWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[256];
        var result = await ws.ReceiveAsync(buf.AsMemory(), ct);
        if (result.MessageType == WebSocketMessageType.Close || result.Count == 0)
            return false;

        var payload = Encoding.UTF8.GetString(buf, 0, result.Count);
        var frame = payload.Split('\u001e', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(frame))
        {
            await SendHandshakeErrorAsync(ws, "Handshake was empty.", ct);
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(frame);
            var protocol = doc.RootElement.TryGetProperty("protocol", out var protocolProp)
                ? protocolProp.GetString()
                : null;
            var version = doc.RootElement.TryGetProperty("version", out var versionProp)
                ? versionProp.GetInt32()
                : 0;

            if (!string.Equals(protocol, "json", StringComparison.OrdinalIgnoreCase) || version != 1)
            {
                await SendHandshakeErrorAsync(ws, "Unsupported SignalR protocol handshake.", ct);
                return false;
            }
        }
        catch (JsonException)
        {
            await SendHandshakeErrorAsync(ws, "Invalid SignalR handshake.", ct);
            return false;
        }

        var response = Encoding.UTF8.GetBytes("{}\u001e");
        await ws.SendAsync(response.AsMemory(), WebSocketMessageType.Text, true);
        return true;
    }

    private static async Task SendHandshakeErrorAsync(CosmoWebSocket ws, string error, CancellationToken ct)
    {
        var response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error }) + "\u001e");
        await ws.SendAsync(response.AsMemory(), WebSocketMessageType.Text, true);
        await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, error);
    }

    internal static object BuildNegotiatePayload(string connectionId) => new
    {
        connectionId,
        connectionToken = connectionId,
        negotiateVersion = 1,
        availableTransports = new[]
        {
            new { transport = "WebSockets", transferFormats = new[] { "Text" } }
        }
    };

    internal static string? ResolveConnectionId(HttpRequest request)
    {
        if (request.Query.TryGetValue("id", out var fromQuery) && !string.IsNullOrWhiteSpace(fromQuery))
            return fromQuery;

        if (request.Query.TryGetValue("connectionToken", out var fromToken) && !string.IsNullOrWhiteSpace(fromToken))
            return fromToken;

        return TryGetQueryValue(request.QueryString, "id")
            ?? TryGetQueryValue(request.QueryString, "connectionToken");
    }

    private static string? TryGetQueryValue(string? raw, string key)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var query = raw.AsSpan();
        if (!query.IsEmpty && query[0] == '?')
            query = query[1..];

        while (!query.IsEmpty)
        {
            var amp = query.IndexOf('&');
            var pair = amp < 0 ? query : query[..amp];
            query = amp < 0 ? ReadOnlySpan<char>.Empty : query[(amp + 1)..];
            if (pair.IsEmpty)
                continue;

            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;

            var candidate = WebUtility.UrlDecode(pair[..eq].ToString());
            if (!candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return WebUtility.UrlDecode(pair[(eq + 1)..].ToString());
        }

        return null;
    }

    private async Task DispatchFrameAsync(THub hub, HubConnection conn, Dictionary<string, CancellationTokenSource> activeStreams, string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("type", out var typeProp)) return;

        switch (typeProp.GetInt32())
        {
            case 1:
                await HandleInvocationAsync(hub, doc.RootElement, conn, ct);
                break;
            case 4:
                await HandleStreamInvocationAsync(hub, doc.RootElement, conn, activeStreams, ct);
                break;
            case 5:
                CancelInvocation(doc.RootElement, activeStreams);
                break;
            case 6: // Ping → Pong
                await conn.SendAsync(Encoding.UTF8.GetBytes("{\"type\":6}\u001e"), ct);
                break;
        }
    }

    private async Task HandleInvocationAsync(THub hub, JsonElement msg, HubConnection conn, CancellationToken ct)
    {
        if (!TryBuildInvocation(msg, ct, out var method, out var invocationId, out var args))
            return;

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

    private async Task HandleStreamInvocationAsync(
        THub hub,
        JsonElement msg,
        HubConnection conn,
        Dictionary<string, CancellationTokenSource> activeStreams,
        CancellationToken ct)
    {
        var invocationId = msg.TryGetProperty("invocationId", out var inv) ? inv.GetString() : null;
        if (string.IsNullOrWhiteSpace(invocationId))
        {
            return;
        }

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        activeStreams[invocationId] = streamCts;

        try
        {
            if (!TryBuildInvocation(msg, streamCts.Token, out var method, out _, out var args))
                return;

            object? result;
            try
            {
                var returnVal = method.Invoke(hub, args);
                if (returnVal is Task task)
                    await task;

                result = UnwrapTaskResult(returnVal);
            }
            catch (Exception ex)
            {
                await SendCompletionErrorAsync(conn, invocationId, ex, ct);
                return;
            }

            if (result is null || !TryGetAsyncEnumerable(result, out var streamInterface))
            {
                await SendCompletionErrorAsync(conn, invocationId, new InvalidOperationException("Hub method did not return a stream."), ct);
                return;
            }

            var itemType = streamInterface.GetGenericArguments()[0];
            var streamMethod = typeof(HubDispatcher<THub>)
                .GetMethod(nameof(StreamAsyncEnumerableAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(itemType);

            var streamTask = (Task)streamMethod.Invoke(null, [result, conn, invocationId, streamCts.Token])!;
            await streamTask;

            if (!streamCts.IsCancellationRequested)
            {
                var completion = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new { type = 3, invocationId }) + "\u001e");
                await conn.SendAsync(completion, ct);
            }
        }
        catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
        {
            var completion = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { type = 3, invocationId }) + "\u001e");
            await conn.SendAsync(completion, ct);
        }
        catch (Exception ex)
        {
            await SendCompletionErrorAsync(conn, invocationId, ex, ct);
        }
        finally
        {
            activeStreams.Remove(invocationId);
        }
    }

    private static async Task StreamAsyncEnumerableAsync<T>(
        IAsyncEnumerable<T> items,
        HubConnection conn,
        string invocationId,
        CancellationToken ct)
    {
        await foreach (var item in items.WithCancellation(ct))
        {
            var payload = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { type = 2, invocationId, item }) + "\u001e");
            await conn.SendAsync(payload, ct);
        }
    }

    private static void CancelInvocation(JsonElement msg, Dictionary<string, CancellationTokenSource> activeStreams)
    {
        var invocationId = msg.TryGetProperty("invocationId", out var inv) ? inv.GetString() : null;
        if (string.IsNullOrWhiteSpace(invocationId))
            return;

        if (activeStreams.TryGetValue(invocationId, out var cts))
            cts.Cancel();
    }

    private bool TryBuildInvocation(JsonElement msg, CancellationToken invocationCt, out MethodInfo method, out string? invocationId, out object?[] args)
    {
        method = null!;
        invocationId = msg.TryGetProperty("invocationId", out var inv) ? inv.GetString() : null;

        var target = msg.TryGetProperty("target", out var t) ? t.GetString() : null;
        if (target is null || !_methods.TryGetValue(target, out method))
        {
            args = [];
            return false;
        }

        var argsEl = msg.TryGetProperty("arguments", out var a) ? a : default;
        var parameters = method.GetParameters();
        args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                args[i] = invocationCt;
            }
            else if (argsEl.ValueKind == JsonValueKind.Array && i < argsEl.GetArrayLength())
            {
                args[i] = argsEl[i].Deserialize(parameters[i].ParameterType, JsonOptions);
            }
        }

        return true;
    }

    private static object? UnwrapTaskResult(object? returnVal)
    {
        if (returnVal is Task task)
            return task.GetType().GetProperty("Result")?.GetValue(task);

        return returnVal;
    }

    private static bool TryGetAsyncEnumerable(object value, out Type streamInterface)
    {
        streamInterface = value.GetType()
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))!;

        return streamInterface is not null;
    }

    private static Task SendCompletionErrorAsync(HubConnection conn, string invocationId, Exception ex, CancellationToken ct)
    {
        var error = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { type = 3, invocationId, error = ex.InnerException?.Message ?? ex.Message }) + "\u001e");
        return conn.SendAsync(error, ct);
    }

    private sealed class HubCallerClientsAdapter(string callerId, HubConnectionManager manager) : IHubCallerClients
    {
        public IClientProxy Caller => manager.Client(callerId);
        public IClientProxy Others => manager.AllExcept([callerId]);
        public IClientProxy OthersInGroup(string g) => manager.GroupExcept(g, [callerId]);

        public IClientProxy All => manager.All;
        public IClientProxy Client(string id) => manager.Client(id);
        public IClientProxy Clients(IEnumerable<string> ids) => manager.Clients(ids);
        public IClientProxy Group(string g) => manager.Group(g);
        public IClientProxy Groups(IEnumerable<string> gs) => manager.Groups(gs);
        public IClientProxy AllExcept(IEnumerable<string> ex) => manager.AllExcept(ex);
    }
}
