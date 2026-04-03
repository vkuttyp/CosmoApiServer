using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CosmoApiServer.Core.Http;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.SignalR;

/// <summary>
/// Handles the full lifecycle of a SignalR hub connection:
/// WebSocket upgrade → handshake → message loop → disconnect.
/// </summary>
public sealed class HubDispatcher<THub> where THub : Hub
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly IReadOnlyDictionary<string, IHubProtocol> Protocols =
        new Dictionary<string, IHubProtocol>(StringComparer.OrdinalIgnoreCase)
        {
            ["json"] = new JsonHubProtocol(),
            ["messagepack"] = new MessagePackHubProtocol()
        };
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
        var protocol = await PerformHandshakeAsync(cosmoSocket, CancellationToken.None);
        if (protocol is null)
            return;

        var hubConn = new HubConnection(connectionId, cosmoSocket);
        hubConn.Protocol = protocol;
        _manager.Add(connectionId, hubConn);

        var hub = ActivatorUtilities.CreateInstance<THub>(httpContext.RequestServices);
        hub.Context = new HubCallerContext(connectionId, httpContext.User, httpContext);
        hub.Clients = new HubCallerClientsAdapter(connectionId, _manager);
        hub.Groups = _manager;

        await hub.OnConnectedAsync();

        Exception? disconnectEx = null;
        var activeStreams = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var backgroundOperations = new ConcurrentBag<Task>();
        try
        {
            var buffer = new byte[64 * 1024];
            byte[] pending = [];
            var binder = new ReflectionInvocationBinder(_methods);

            while (true)
            {
                WebSocketReceiveResult result;
                try { result = await cosmoSocket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None); }
                catch (OperationCanceledException) { break; }

                if (result.MessageType == WebSocketMessageType.Close) break;

                pending = Append(pending, buffer.AsSpan(0, result.Count));
                var sequence = new ReadOnlySequence<byte>(pending);

                while (protocol.TryParseMessage(ref sequence, binder, out var message))
                {
                    await DispatchMessageAsync(hub, hubConn, activeStreams, backgroundOperations, message, CancellationToken.None);
                }

                pending = sequence.ToArray();
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

            while (backgroundOperations.TryTake(out var op))
            {
                try { await op; } catch { }
            }

            foreach (var streamCts in activeStreams.Values)
                streamCts.Dispose();

            _manager.Remove(connectionId);
            await hub.OnDisconnectedAsync(disconnectEx);
            await hub.DisposeAsync();
            try { await cosmoSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed"); } catch { }
        }
    }

    private static async Task<IHubProtocol?> PerformHandshakeAsync(CosmoWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[256];
        var result = await ws.ReceiveAsync(buf.AsMemory(), ct);
        if (result.MessageType == WebSocketMessageType.Close || result.Count == 0)
            return null;

        var payload = new ReadOnlySequence<byte>(buf.AsMemory(0, result.Count));
        if (!HandshakeProtocol.TryParseRequestMessage(ref payload, out var request) || request is null)
        {
            await SendHandshakeErrorAsync(ws, "Invalid SignalR handshake.", ct);
            return null;
        }

        if (!Protocols.TryGetValue(request.Protocol, out var protocol) || !protocol.IsVersionSupported(request.Version))
        {
            await SendHandshakeErrorAsync(ws, "Unsupported SignalR protocol handshake.", ct);
            return null;
        }

        var response = HandshakeProtocol.GetSuccessfulHandshake(protocol);
        await ws.SendAsync(response.ToArray().AsMemory(), WebSocketMessageType.Text, true);
        return protocol;
    }

    private static async Task SendHandshakeErrorAsync(CosmoWebSocket ws, string error, CancellationToken ct)
    {
        var response = new HandshakeResponseMessage(error);
        var buffer = new ArrayBufferWriter<byte>();
        HandshakeProtocol.WriteResponseMessage(response, buffer);
        await ws.SendAsync(buffer.WrittenMemory, WebSocketMessageType.Text, true);
        await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, error);
    }

    internal static object BuildNegotiatePayload(string connectionId) => new
    {
        connectionId,
        connectionToken = connectionId,
        negotiateVersion = 1,
        availableTransports = new[]
        {
            new { transport = "WebSockets", transferFormats = new[] { "Text", "Binary" } }
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

    private async Task DispatchMessageAsync(
        THub hub,
        HubConnection conn,
        ConcurrentDictionary<string, CancellationTokenSource> activeStreams,
        ConcurrentBag<Task> backgroundOperations,
        HubMessage message,
        CancellationToken ct)
    {
        switch (message)
        {
            case InvocationMessage invocation:
                await HandleInvocationAsync(hub, invocation, conn, ct);
                break;
            case StreamInvocationMessage streamInvocation:
                var streamTask = HandleStreamInvocationAsync(hub, streamInvocation, conn, activeStreams, ct);
                backgroundOperations.Add(streamTask);
                break;
            case CancelInvocationMessage cancel:
                CancelInvocation(cancel, activeStreams);
                break;
            case PingMessage:
                await conn.SendHubMessageAsync(PingMessage.Instance, ct);
                break;
            case InvocationBindingFailureMessage bindFailure:
                if (!string.IsNullOrWhiteSpace(bindFailure.InvocationId))
                    await conn.SendHubMessageAsync(CompletionMessage.WithError(bindFailure.InvocationId, bindFailure.BindingFailure.SourceException.Message), ct);
                break;
        }
    }

    private async Task HandleInvocationAsync(THub hub, InvocationMessage msg, HubConnection conn, CancellationToken ct)
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

                await conn.SendHubMessageAsync(
                    result is null ? CompletionMessage.Empty(invocationId) : CompletionMessage.WithResult(invocationId, result),
                    ct);
            }
        }
        catch (Exception ex)
        {
            if (invocationId is not null)
            {
                await conn.SendHubMessageAsync(CompletionMessage.WithError(invocationId, ex.InnerException?.Message ?? ex.Message), ct);
            }
        }
    }

    private async Task HandleStreamInvocationAsync(
        THub hub,
        StreamInvocationMessage msg,
        HubConnection conn,
        ConcurrentDictionary<string, CancellationTokenSource> activeStreams,
        CancellationToken ct)
    {
        var invocationId = msg.InvocationId;
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
                await conn.SendHubMessageAsync(CompletionMessage.Empty(invocationId), ct);
            }
        }
        catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
        {
            await conn.SendHubMessageAsync(CompletionMessage.Empty(invocationId), ct);
        }
        catch (Exception ex)
        {
            await SendCompletionErrorAsync(conn, invocationId, ex, ct);
        }
        finally
        {
            activeStreams.TryRemove(invocationId, out _);
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
            await conn.SendHubMessageAsync(new StreamItemMessage(invocationId, item), ct);
        }
    }

    private static void CancelInvocation(CancelInvocationMessage msg, ConcurrentDictionary<string, CancellationTokenSource> activeStreams)
    {
        var invocationId = msg.InvocationId;
        if (string.IsNullOrWhiteSpace(invocationId))
            return;

        if (activeStreams.TryGetValue(invocationId, out var cts))
            cts.Cancel();
    }

    private bool TryBuildInvocation(HubMethodInvocationMessage msg, CancellationToken invocationCt, out MethodInfo method, out string? invocationId, out object?[] args)
    {
        method = null!;
        invocationId = msg.InvocationId;

        var target = msg.Target;
        if (target is null || !_methods.TryGetValue(target, out method))
        {
            args = [];
            return false;
        }

        var incomingArgs = msg.Arguments;
        var parameters = method.GetParameters();
        args = new object?[parameters.Length];
        var nonCancellationIndex = 0;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                args[i] = invocationCt;
            }
            else if (nonCancellationIndex < incomingArgs.Length)
            {
                args[i] = incomingArgs[nonCancellationIndex++];
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
        return conn.SendHubMessageAsync(CompletionMessage.WithError(invocationId, ex.InnerException?.Message ?? ex.Message), ct);
    }

    private static byte[] Append(byte[] existing, ReadOnlySpan<byte> next)
    {
        var combined = new byte[existing.Length + next.Length];
        existing.CopyTo(combined, 0);
        next.CopyTo(combined.AsSpan(existing.Length));
        return combined;
    }

    private sealed class ReflectionInvocationBinder(Dictionary<string, MethodInfo> methods) : IInvocationBinder
    {
        public Type GetReturnType(string invocationId) => typeof(object);

        public IReadOnlyList<Type> GetParameterTypes(string methodName)
        {
            if (!methods.TryGetValue(methodName, out var method))
                return [];

            return method.GetParameters()
                .Where(p => p.ParameterType != typeof(CancellationToken))
                .Select(p => p.ParameterType)
                .ToArray();
        }

        public Type GetStreamItemType(string streamId) => typeof(object);

        public string? GetTarget(ReadOnlySpan<byte> targetUtf8Bytes) =>
            Encoding.UTF8.GetString(targetUtf8Bytes);
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
