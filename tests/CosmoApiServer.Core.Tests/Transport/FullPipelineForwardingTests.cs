using System.Net;
using System.Net.Sockets;
using System.Text;
using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Transport;

namespace CosmoApiServer.Core.Tests.Transport;

/// <summary>
/// Tests that go through the full CosmoWebApplication pipeline:
/// CosmoWebApplicationBuilder → Build → MapGet/MapPost → Run → HTTP client.
/// This tests the complete middleware + router + Http11Connection stack.
/// </summary>
public class FullPipelineForwardingTests
{
    [Fact]
    public async Task Get_ThroughFullPipeline_ReturnsResponse()
    {
        var response = await RunFullPipelineRequest(
            app =>
            {
                app.MapGet("/{**path}", ctx =>
                {
                    ctx.Response.WriteText("get-ok");
                    return ValueTask.CompletedTask;
                });
            },
            "GET /test HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains("get-ok", response);
    }

    [Fact]
    public async Task Post_SmallBody_ThroughFullPipeline_BodyIsAvailable()
    {
        string? body = null;
        var response = await RunFullPipelineRequest(
            app =>
            {
                app.MapPost("/{**path}", ctx =>
                {
                    body = Encoding.UTF8.GetString(ctx.Request.Body);
                    ctx.Response.WriteText($"echo:{body}");
                    return ValueTask.CompletedTask;
                });
            },
            "POST /test HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Type: text/plain\r\n" +
            "Content-Length: 11\r\n" +
            "Connection: close\r\n\r\n" +
            "hello world");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Equal("hello world", body);
        Assert.Contains("echo:hello world", response);
    }

    [Fact]
    public async Task Post_XmlBody_ThroughFullPipeline()
    {
        string? body = null;
        var xml = "<Autodiscover><Request><EMailAddress>test@test.com</EMailAddress></Request></Autodiscover>";

        var response = await RunFullPipelineRequest(
            app =>
            {
                app.MapPost("/{**path}", ctx =>
                {
                    body = Encoding.UTF8.GetString(ctx.Request.Body);
                    ctx.Response.WriteText("ok");
                    return ValueTask.CompletedTask;
                });
            },
            $"POST /autodiscover/autodiscover.xml HTTP/1.1\r\n" +
            $"Host: localhost\r\n" +
            $"Content-Type: text/xml\r\n" +
            $"Content-Length: {xml.Length}\r\n" +
            $"Connection: close\r\n\r\n" +
            xml);

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.NotNull(body);
        Assert.Contains("test@test.com", body);
    }

    [Fact]
    public async Task Post_WithBothGetAndPostRoutes_PostIsMatched()
    {
        string? method = null;
        RequestDelegate handler = ctx =>
        {
            method = ctx.Request.Method.ToString();
            ctx.Response.WriteText($"method:{method}");
            return ValueTask.CompletedTask;
        };

        var response = await RunFullPipelineRequest(
            app =>
            {
                app.MapGet("/{**path}", handler);
                app.MapPost("/{**path}", handler);
                app.MapPut("/{**path}", handler);
                app.MapDelete("/{**path}", handler);
                app.MapPatch("/{**path}", handler);
            },
            "POST /test HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Type: text/plain\r\n" +
            "Content-Length: 4\r\n" +
            "Connection: close\r\n\r\n" +
            "test");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Equal("POST", method);
        Assert.Contains("method:POST", response);
    }

    [Fact]
    public async Task Redirect_301_ThroughFullPipeline()
    {
        var response = await RunFullPipelineRequest(
            app =>
            {
                app.MapGet("/{**path}", ctx =>
                {
                    ctx.Response.StatusCode = 301;
                    ctx.Response.Headers["Location"] = "https://example.com/new";
                    return ValueTask.CompletedTask;
                });
            },
            "GET /old HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 301", response);
        Assert.Contains("Location: https://example.com/new", response);
    }

    [Fact]
    public async Task Options_ThroughFullPipeline_Returns404_WhenNotMapped()
    {
        // OPTIONS is not mapped — should return 404
        var response = await RunFullPipelineRequest(
            app =>
            {
                app.MapGet("/{**path}", ctx =>
                {
                    ctx.Response.WriteText("get-only");
                    return ValueTask.CompletedTask;
                });
            },
            "OPTIONS /test HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 404", response);
    }

    [Fact]
    public async Task Post_WithForwarder_ThroughFullPipeline_ForwardsBody()
    {
        // Start upstream echo server
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;

        using var upstreamCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = Task.Run(async () =>
        {
            using var socket = await upstream.AcceptSocketAsync(upstreamCts.Token);
            using var stream = new NetworkStream(socket, ownsSocket: true);
            var sb = new StringBuilder();
            var buf = new byte[4096];

            while (true)
            {
                int read = await stream.ReadAsync(buf, upstreamCts.Token);
                if (read == 0) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, read));
                if (sb.ToString().Contains("\r\n\r\n")) break;
            }

            var raw = sb.ToString();
            var bodyStart = raw.IndexOf("\r\n\r\n") + 4;
            // Read remaining body based on Content-Length
            var clMatch = System.Text.RegularExpressions.Regex.Match(raw, @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (clMatch.Success)
            {
                int cl = int.Parse(clMatch.Groups[1].Value);
                while (sb.Length - bodyStart < cl)
                {
                    int read = await stream.ReadAsync(buf, upstreamCts.Token);
                    if (read == 0) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, read));
                }
            }

            raw = sb.ToString();
            bodyStart = raw.IndexOf("\r\n\r\n") + 4;
            var body = bodyStart < raw.Length ? raw[bodyStart..] : "";

            var respBody = $"upstream-echo:{body}";
            var resp = $"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetByteCount(respBody)}\r\nConnection: close\r\n\r\n{respBody}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(resp), upstreamCts.Token);
        }, upstreamCts.Token);

        var forwarder = new PipelineHttpForwarder();
        var xmlBody = "<Request><Email>test@test.com</Email></Request>";

        var response = await RunFullPipelineRequest(
            app =>
            {
                app.MapGet("/{**path}", async ctx =>
                {
                    await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upstreamPort, ctx.Request.Path, null, null, ctx.RequestAborted);
                });
                app.MapPost("/{**path}", async ctx =>
                {
                    await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upstreamPort, ctx.Request.Path, null, null, ctx.RequestAborted);
                });
            },
            $"POST /autodiscover HTTP/1.1\r\n" +
            $"Host: localhost\r\n" +
            $"Content-Type: text/xml\r\n" +
            $"Content-Length: {xmlBody.Length}\r\n" +
            $"Connection: close\r\n\r\n" +
            xmlBody);

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains($"upstream-echo:{xmlBody}", response);

        await forwarder.DisposeAsync();
        upstreamCts.Cancel();
        try { await upstreamTask; } catch { }
    }

    [Fact]
    public async Task Authorization_ThroughFullPipeline_IsAvailable()
    {
        string? auth = null;
        var response = await RunFullPipelineRequest(
            app =>
            {
                app.MapPost("/{**path}", ctx =>
                {
                    auth = ctx.Request.Authorization;
                    ctx.Response.WriteText("ok");
                    return ValueTask.CompletedTask;
                });
            },
            "POST /api HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Authorization: Basic dGVzdDpwYXNz\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Equal("Basic dGVzdDpwYXNz", auth);
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static async Task<string> RunFullPipelineRequest(
        Action<CosmoWebApplication> configureApp,
        string rawRequest,
        int timeoutSeconds = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        // Use a TCP listener to pick a free port
        using var portFinder = new TcpListener(IPAddress.Loopback, 0);
        portFinder.Start();
        int port = ((IPEndPoint)portFinder.LocalEndpoint).Port;
        portFinder.Stop();

        var builder = CosmoWebApplicationBuilder.Create()
            .ListenOn(port);

        var app = builder.Build();
        configureApp(app);

        var serverTask = Task.Run(() => app.RunAsync(cts.Token), cts.Token);

        // Give the server time to start
        await Task.Delay(300, cts.Token);

        // Send request
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(rawRequest), cts.Token);
        await stream.FlushAsync(cts.Token);

        var sb = new StringBuilder();
        var buf = new byte[8192];

        while (true)
        {
            int read = await stream.ReadAsync(buf, cts.Token);
            if (read == 0) break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, read));
        }

        cts.Cancel();
        try { await serverTask; } catch { }

        return sb.ToString();
    }
}
