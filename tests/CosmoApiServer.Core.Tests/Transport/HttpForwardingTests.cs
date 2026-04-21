using System.Net;
using System.Net.Sockets;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Tests.Transport;

/// <summary>
/// End-to-end HTTP forwarding tests through Http11Connection.
/// Covers: POST body forwarding, OPTIONS, PUT, DELETE, redirects,
/// headers, query strings, and PipelineHttpForwarder proxy path.
/// </summary>
public class HttpForwardingTests
{
    // ── POST body forwarding ────────────────────────────────────────────────

    [Fact]
    public async Task Post_SmallBody_IsBufferedAndAvailable()
    {
        string? receivedBody = null;
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                receivedBody = Encoding.UTF8.GetString(ctx.Request.Body);
                ctx.Response.WriteText($"echo:{receivedBody}");
                return ValueTask.CompletedTask;
            },
            "POST /test HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Type: text/plain\r\n" +
            "Content-Length: 11\r\n" +
            "Connection: close\r\n\r\n" +
            "hello world");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains("echo:hello world", response);
        Assert.Equal("hello world", receivedBody);
    }

    [Fact]
    public async Task Post_XmlBody_IsReceivedCorrectly()
    {
        string? receivedBody = null;
        var xml = "<Autodiscover><Request><EMailAddress>test@test.com</EMailAddress></Request></Autodiscover>";

        var response = await RunServerAndSendRequest(
            ctx =>
            {
                receivedBody = Encoding.UTF8.GetString(ctx.Request.Body);
                ctx.Response.WriteText("ok");
                return ValueTask.CompletedTask;
            },
            $"POST /autodiscover/autodiscover.xml HTTP/1.1\r\n" +
            $"Host: localhost\r\n" +
            $"Content-Type: text/xml\r\n" +
            $"Content-Length: {xml.Length}\r\n" +
            $"Connection: close\r\n\r\n" +
            xml);

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.NotNull(receivedBody);
        Assert.Contains("test@test.com", receivedBody);
    }

    [Fact]
    public async Task Post_EmptyBody_ContentLengthZero_DoesNotHang()
    {
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                ctx.Response.WriteText($"body:{ctx.Request.Body.Length}");
                return ValueTask.CompletedTask;
            },
            "POST /empty HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains("body:0", response);
    }

    [Fact]
    public async Task Post_LargeBody_IsAvailableViaBodyStream()
    {
        var bigBody = new string('X', 100_000);
        int receivedLength = 0;

        var response = await RunServerAndSendRequest(
            async ctx =>
            {
                if (ctx.Request.BodyReader is not null)
                {
                    var ms = new MemoryStream();
                    await ctx.Request.BodyStream.CopyToAsync(ms);
                    receivedLength = (int)ms.Length;
                }
                else
                {
                    receivedLength = ctx.Request.Body.Length;
                }
                ctx.Response.WriteText($"len:{receivedLength}");
            },
            $"POST /upload HTTP/1.1\r\n" +
            $"Host: localhost\r\n" +
            $"Content-Type: application/octet-stream\r\n" +
            $"Content-Length: {bigBody.Length}\r\n" +
            $"Connection: close\r\n\r\n" +
            bigBody);

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains($"len:{bigBody.Length}", response);
    }

    // ── PUT with body ───────────────────────────────────────────────────────

    [Fact]
    public async Task Put_WithJsonBody_IsReceived()
    {
        string? receivedBody = null;
        var json = "{\"name\":\"test\"}";

        var response = await RunServerAndSendRequest(
            ctx =>
            {
                receivedBody = Encoding.UTF8.GetString(ctx.Request.Body);
                ctx.Response.WriteText("ok");
                return ValueTask.CompletedTask;
            },
            $"PUT /resource HTTP/1.1\r\n" +
            $"Host: localhost\r\n" +
            $"Content-Type: application/json\r\n" +
            $"Content-Length: {json.Length}\r\n" +
            $"Connection: close\r\n\r\n" +
            json);

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Equal(json, receivedBody);
    }

    // ── DELETE ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Returns204()
    {
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                ctx.Response.StatusCode = 204;
                return ValueTask.CompletedTask;
            },
            "DELETE /resource/123 HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 204", response);
    }

    // ── OPTIONS ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Options_IsHandledCorrectly()
    {
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                ctx.Response.Headers["Allow"] = "GET, POST, OPTIONS";
                ctx.Response.Headers["MS-ASProtocolVersions"] = "14.1";
                return ValueTask.CompletedTask;
            },
            "OPTIONS /Microsoft-Server-ActiveSync HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains("MS-ASProtocolVersions: 14.1", response);
    }

    // ── Redirect (headers only, no body) ────────────────────────────────────

    [Fact]
    public async Task Redirect_301_WritesLocationHeader()
    {
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                ctx.Response.StatusCode = 301;
                ctx.Response.Headers["Location"] = "https://example.com/new";
                return ValueTask.CompletedTask;
            },
            "GET /old HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 301", response);
        Assert.Contains("Location: https://example.com/new", response);
    }

    [Fact]
    public async Task EmptyResponse_200_NoBody()
    {
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                ctx.Response.StatusCode = 200;
                return ValueTask.CompletedTask;
            },
            "GET /empty HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
    }

    // ── Request headers ─────────────────────────────────────────────────────

    [Fact]
    public async Task AuthorizationHeader_IsAvailable()
    {
        string? auth = null;
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                auth = ctx.Request.Authorization;
                ctx.Response.WriteText("ok");
                return ValueTask.CompletedTask;
            },
            "GET /secure HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Authorization: Basic dGVzdDp0ZXN0\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Equal("Basic dGVzdDp0ZXN0", auth);
    }

    [Fact]
    public async Task ContentType_IsAvailable()
    {
        string? ct = null;
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                ct = ctx.Request.ContentType;
                ctx.Response.WriteText("ok");
                return ValueTask.CompletedTask;
            },
            "POST /api HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Type: application/vnd.ms-sync.wbxml\r\n" +
            "Content-Length: 4\r\n" +
            "Connection: close\r\n\r\n" +
            "test");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Equal("application/vnd.ms-sync.wbxml", ct);
    }

    [Fact]
    public async Task CustomHeaders_AreAvailable()
    {
        string? custom = null;
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                ctx.Request.Headers.TryGetValue("X-Custom-Header", out var val);
                custom = val;
                ctx.Response.WriteText("ok");
                return ValueTask.CompletedTask;
            },
            "GET /test HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "X-Custom-Header: my-value\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Equal("my-value", custom);
    }

    // ── Query string ────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryString_IsPreserved()
    {
        string? qs = null;
        var response = await RunServerAndSendRequest(
            ctx =>
            {
                qs = ctx.Request.QueryString;
                ctx.Response.WriteText("ok");
                return ValueTask.CompletedTask;
            },
            "POST /Microsoft-Server-ActiveSync?Cmd=FolderSync&User=test&DeviceId=abc HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.NotNull(qs);
        Assert.Contains("Cmd=FolderSync", qs);
        Assert.Contains("DeviceId=abc", qs);
    }

    // ── PipelineHttpForwarder ───────────────────────────────────────────────

    [Fact]
    public async Task Forwarder_Post_ForwardsBodyToUpstream()
    {
        // Start an upstream echo server
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;

        using var upstreamCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = Task.Run(() => RunUpstreamEchoServer(upstream, upstreamCts.Token), upstreamCts.Token);

        var forwarder = new PipelineHttpForwarder();
        var xmlBody = "<Request><Email>test@test.com</Email></Request>";

        var response = await RunServerAndSendRequest(
            async ctx =>
            {
                var path = ctx.Request.Path + (string.IsNullOrEmpty(ctx.Request.QueryString) ? "" : "?" + ctx.Request.QueryString);
                await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upstreamPort, path, null, null, ctx.RequestAborted);
            },
            $"POST /autodiscover HTTP/1.1\r\n" +
            $"Host: localhost\r\n" +
            $"Content-Type: text/xml\r\n" +
            $"Content-Length: {xmlBody.Length}\r\n" +
            $"Connection: close\r\n\r\n" +
            xmlBody);

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains(xmlBody, response);

        await forwarder.DisposeAsync();
        upstreamCts.Cancel();
        try { await upstreamTask; } catch { }
    }

    [Fact]
    public async Task Forwarder_Get_ForwardsAndReturnsResponse()
    {
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;

        using var upstreamCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = Task.Run(() => RunUpstreamEchoServer(upstream, upstreamCts.Token), upstreamCts.Token);

        var forwarder = new PipelineHttpForwarder();

        var response = await RunServerAndSendRequest(
            async ctx =>
            {
                await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upstreamPort, ctx.Request.Path, null, null, ctx.RequestAborted);
            },
            "GET /health HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);

        await forwarder.DisposeAsync();
        upstreamCts.Cancel();
        try { await upstreamTask; } catch { }
    }

    [Fact]
    public async Task Forwarder_AuthorizationHeader_IsForwarded()
    {
        string? receivedAuth = null;

        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;

        using var upstreamCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = Task.Run(async () =>
        {
            using var socket = await upstream.AcceptSocketAsync(upstreamCts.Token);
            using var stream = new NetworkStream(socket, ownsSocket: true);
            var buf = new byte[4096];
            int read = await stream.ReadAsync(buf, upstreamCts.Token);
            var raw = Encoding.UTF8.GetString(buf, 0, read);
            receivedAuth = raw.Split('\n')
                .FirstOrDefault(l => l.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)[1]?.Trim();

            var resp = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(resp), upstreamCts.Token);
        }, upstreamCts.Token);

        var forwarder = new PipelineHttpForwarder();

        var response = await RunServerAndSendRequest(
            async ctx =>
            {
                await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upstreamPort, ctx.Request.Path, null, null, ctx.RequestAborted);
            },
            "POST /api HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Authorization: Basic dGVzdDpwYXNz\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Equal("Basic dGVzdDpwYXNz", receivedAuth);

        await forwarder.DisposeAsync();
        upstreamCts.Cancel();
        try { await upstreamTask; } catch { }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<string> RunServerAndSendRequest(
        RequestDelegate handler,
        string rawRequest,
        int timeoutSeconds = 10)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync(cts.Token);
            using var stream = new NetworkStream(socket, ownsSocket: true);
            await Http11Connection.RunAsync(
                stream, handler, services,
                maxBodySize: 1024 * 1024,
                enableHttp2: false,
                remoteIp: "127.0.0.1",
                cts.Token);
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        await using var clientStream = client.GetStream();

        await clientStream.WriteAsync(Encoding.ASCII.GetBytes(rawRequest), cts.Token);
        await clientStream.FlushAsync(cts.Token);

        var sb = new StringBuilder();
        var buf = new byte[8192];

        while (true)
        {
            int read = await clientStream.ReadAsync(buf, cts.Token);
            if (read == 0) break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, read));
        }

        try { await serverTask; } catch { }
        return sb.ToString();
    }

    private static async Task RunUpstreamEchoServer(TcpListener listener, CancellationToken ct)
    {
        using var socket = await listener.AcceptSocketAsync(ct);
        using var stream = new NetworkStream(socket, ownsSocket: true);
        var buf = new byte[65536];
        var sb = new StringBuilder();

        // Read full request (headers + body)
        while (true)
        {
            int read = await stream.ReadAsync(buf, ct);
            if (read == 0) break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, read));

            var raw = sb.ToString();
            var headerEnd = raw.IndexOf("\r\n\r\n");
            if (headerEnd < 0) continue;

            // Check Content-Length to know when body is complete
            var clMatch = System.Text.RegularExpressions.Regex.Match(raw, @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (clMatch.Success)
            {
                int cl = int.Parse(clMatch.Groups[1].Value);
                int bodyReceived = raw.Length - (headerEnd + 4);
                if (bodyReceived >= cl) break;
            }
            else
            {
                break; // No Content-Length = no body
            }
        }

        var request = sb.ToString();
        var bodyStart = request.IndexOf("\r\n\r\n") + 4;
        var body = bodyStart < request.Length ? request[bodyStart..] : "";

        // Echo: return method + path + body
        var firstLine = request.Split('\n')[0].Trim();
        var echoBody = $"{firstLine}\r\n{body}";
        var response = $"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetByteCount(echoBody)}\r\nConnection: close\r\n\r\n{echoBody}";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
    }
}
