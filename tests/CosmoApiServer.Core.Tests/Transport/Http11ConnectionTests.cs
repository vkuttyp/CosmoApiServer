using System.Net;
using System.Net.Sockets;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Tests.Transport;

public class Http11ConnectionTests
{
    [Fact]
    public async Task RunAsync_WritesBasicChunkedTextResponse()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync(cts.Token);
            using var stream = new NetworkStream(socket, ownsSocket: true);
            await Http11Connection.RunAsync(
                stream,
                ctx =>
                {
                    ctx.Response.WriteText("pong");
                    return ValueTask.CompletedTask;
                },
                services,
                maxBodySize: 1024 * 1024,
                enableHttp2: false,
                remoteIp: null,
                cts.Token);
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        await using var clientStream = client.GetStream();

        byte[] request = Encoding.ASCII.GetBytes(
            "GET /ping HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n");

        await clientStream.WriteAsync(request, cts.Token);
        await clientStream.FlushAsync(cts.Token);

        string response = await ReadToEndAsync(clientStream, cts.Token);

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains("Transfer-Encoding: chunked", response);
        Assert.Contains("4\r\npong\r\n0\r\n\r\n", response);

        await serverTask;
    }

    [Fact]
    public async Task RunAsync_KeepAliveRequest_FlushesChunkedTextResponseWithoutConnectionClose()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync(cts.Token);
            using var stream = new NetworkStream(socket, ownsSocket: true);
            await Http11Connection.RunAsync(
                stream,
                ctx =>
                {
                    ctx.Response.WriteText("pong");
                    return ValueTask.CompletedTask;
                },
                services,
                maxBodySize: 1024 * 1024,
                enableHttp2: false,
                remoteIp: null,
                cts.Token);
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        await using var clientStream = client.GetStream();

        byte[] request = Encoding.ASCII.GetBytes(
            "GET /ping HTTP/1.1\r\n" +
            "Host: localhost\r\n\r\n");

        await clientStream.WriteAsync(request, cts.Token);
        await clientStream.FlushAsync(cts.Token);

        string response = await ReadUntilAsync(clientStream, "0\r\n\r\n", cts.Token);

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains("Transfer-Encoding: chunked", response);
        Assert.Contains("4\r\npong\r\n0\r\n\r\n", response);

        client.Close();
        await serverTask;
    }

    private static async Task<string> ReadToEndAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        while (true)
        {
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                break;

            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }

        return sb.ToString();
    }

    private static async Task<string> ReadUntilAsync(NetworkStream stream, string marker, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        while (!sb.ToString().Contains(marker, StringComparison.Ordinal))
        {
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                break;

            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }

        return sb.ToString();
    }

    // ── Alt-Svc injection integration tests ───────────────────────────────

    [Fact]
    public async Task RunAsync_WithAltSvcValue_ResponseContainsAltSvcHeader()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        const string altSvc = "h3=\":443\"; ma=86400";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync(cts.Token);
            using var stream = new NetworkStream(socket, ownsSocket: true);
            await Http11Connection.RunAsync(
                stream,
                ctx => { ctx.Response.WriteText("ok"); return ValueTask.CompletedTask; },
                services,
                maxBodySize: 1024 * 1024,
                enableHttp2: false,
                remoteIp: null,
                cts.Token,
                altSvcValue: altSvc);
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        await using var clientStream = client.GetStream();

        byte[] request = Encoding.ASCII.GetBytes(
            "GET /ping HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
        await clientStream.WriteAsync(request, cts.Token);

        string response = await ReadToEndAsync(clientStream, cts.Token);

        Assert.Contains("Alt-Svc:", response);
        Assert.Contains(altSvc, response);

        await serverTask;
    }

    [Fact]
    public async Task RunAsync_WithoutAltSvcValue_ResponseOmitsAltSvcHeader()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync(cts.Token);
            using var stream = new NetworkStream(socket, ownsSocket: true);
            await Http11Connection.RunAsync(
                stream,
                ctx => { ctx.Response.WriteText("ok"); return ValueTask.CompletedTask; },
                services,
                maxBodySize: 1024 * 1024,
                enableHttp2: false,
                remoteIp: null,
                cts.Token,
                altSvcValue: null);
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        await using var clientStream = client.GetStream();

        byte[] request = Encoding.ASCII.GetBytes(
            "GET /ping HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
        await clientStream.WriteAsync(request, cts.Token);

        string response = await ReadToEndAsync(clientStream, cts.Token);

        Assert.DoesNotContain("Alt-Svc:", response);

        await serverTask;
    }

    [Fact]
    public async Task RunAsync_AltSvcNotDuplicated_WhenResponseAlreadySetsIt()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        const string serverAltSvc = "h3=\":443\"; ma=86400";
        const string appAltSvc = "h3=\":8443\"; ma=3600";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync(cts.Token);
            using var stream = new NetworkStream(socket, ownsSocket: true);
            await Http11Connection.RunAsync(
                stream,
                ctx =>
                {
                    // App sets its own Alt-Svc — the server-injected one should be suppressed
                    ctx.Response.Headers["Alt-Svc"] = appAltSvc;
                    ctx.Response.WriteText("ok");
                    return ValueTask.CompletedTask;
                },
                services,
                maxBodySize: 1024 * 1024,
                enableHttp2: false,
                remoteIp: null,
                cts.Token,
                altSvcValue: serverAltSvc);
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        await using var clientStream = client.GetStream();

        byte[] request = Encoding.ASCII.GetBytes(
            "GET /ping HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
        await clientStream.WriteAsync(request, cts.Token);

        string response = await ReadToEndAsync(clientStream, cts.Token);

        // App's Alt-Svc should appear; server's injected one should not
        Assert.Contains(appAltSvc, response);
        Assert.DoesNotContain(serverAltSvc, response);

        await serverTask;
    }
}
