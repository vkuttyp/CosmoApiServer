using System.Net;
using System.Net.Sockets;
using System.Text;
using CosmoApiServer.Core.Hosting;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Transport;

namespace CosmoApiServer.Core.Tests.Transport;

/// <summary>
/// Tests simulating CosmoProxy's exact routing pattern:
/// - Multiple MapGet/MapPost/etc on catch-all "/{**path}"
/// - AutoHttps redirect logic for host-based routes
/// - Forwarding POST bodies through the proxy handler
/// </summary>
public class ProxyPatternTests
{
    [Fact]
    public async Task ProxyPattern_PostWithAutoHttpsRedirect_BodyIsForwarded()
    {
        // Simulate CosmoProxy: on HTTPS port, no redirect needed, POST goes to proxy handler
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
                var raw = sb.ToString();
                if (!raw.Contains("\r\n\r\n")) continue;
                var clMatch = System.Text.RegularExpressions.Regex.Match(raw, @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (clMatch.Success)
                {
                    int cl = int.Parse(clMatch.Groups[1].Value);
                    int bodyStart = raw.IndexOf("\r\n\r\n") + 4;
                    while (sb.Length - bodyStart < cl)
                    {
                        read = await stream.ReadAsync(buf, upstreamCts.Token);
                        if (read == 0) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, read));
                    }
                }
                break;
            }
            var request = sb.ToString();
            var bIdx = request.IndexOf("\r\n\r\n") + 4;
            var body = bIdx < request.Length ? request[bIdx..] : "";
            var respBody = $"proxied:{body}";
            var resp = $"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetByteCount(respBody)}\r\nConnection: close\r\n\r\n{respBody}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(resp), upstreamCts.Token);
        }, upstreamCts.Token);

        var forwarder = new PipelineHttpForwarder();
        var upPort = upstreamPort;
        var xmlBody = "<Autodiscover><Request><EMailAddress>kutty@murshisoft.com</EMailAddress></Request></Autodiscover>";

        var response = await RunFullPipelineRequest(
            app =>
            {
                // API endpoint (registered first, like CosmoProxy)
                app.MapGet("/api/health", ctx =>
                {
                    ctx.Response.WriteJson(new { status = "ok" });
                    return ValueTask.CompletedTask;
                });

                // Catch-all proxy routes (registered last, like CosmoProxy)
                RequestDelegate proxyHandler = async ctx =>
                {
                    var host = ctx.Request.Host ?? "localhost";
                    var path = ctx.Request.Path + (string.IsNullOrEmpty(ctx.Request.QueryString) ? "" : "?" + ctx.Request.QueryString);

                    // Simulate AutoHttpsRedirect — on HTTP port, redirect to HTTPS
                    // (In this test, we simulate being on HTTPS so no redirect)
                    var isHttps = ctx.Items.TryGetValue("__IsHttps", out var v) && v is true;
                    // Skip redirect for this test

                    await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upPort, path, null, null, ctx.RequestAborted);
                };

                app.MapGet("/{**path}", proxyHandler);
                app.MapPost("/{**path}", proxyHandler);
                app.MapPut("/{**path}", proxyHandler);
                app.MapDelete("/{**path}", proxyHandler);
                app.MapPatch("/{**path}", proxyHandler);
            },
            $"POST /autodiscover/autodiscover.xml HTTP/1.1\r\n" +
            $"Host: autodiscover.murshisoft.com\r\n" +
            $"Content-Type: text/xml\r\n" +
            $"Content-Length: {xmlBody.Length}\r\n" +
            $"Connection: close\r\n\r\n" +
            xmlBody);

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains($"proxied:{xmlBody}", response);

        await forwarder.DisposeAsync();
        upstreamCts.Cancel();
        try { await upstreamTask; } catch { }
    }

    [Fact]
    public async Task ProxyPattern_ActiveSyncOptions_ThroughProxy()
    {
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;

        using var upstreamCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upstreamTask = Task.Run(async () =>
        {
            using var socket = await upstream.AcceptSocketAsync(upstreamCts.Token);
            using var stream = new NetworkStream(socket, ownsSocket: true);
            var buf = new byte[4096];
            await stream.ReadAsync(buf, upstreamCts.Token);
            var resp = "HTTP/1.1 200 OK\r\nMS-ASProtocolVersions: 14.1\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(resp), upstreamCts.Token);
        }, upstreamCts.Token);

        var forwarder = new PipelineHttpForwarder();
        var upPort = upstreamPort;

        // Note: OPTIONS is not mapped via MapOptions — it should return 404
        // unless we add a special mapping
        var response = await RunFullPipelineRequest(
            app =>
            {
                // Map OPTIONS explicitly for ActiveSync
                app.MapGet("/{**path}", async ctx =>
                {
                    if (ctx.Request.Method == CosmoApiServer.Core.Http.HttpMethod.OPTIONS)
                    {
                        await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upPort, ctx.Request.Path, null, null, ctx.RequestAborted);
                        return;
                    }
                    await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upPort, ctx.Request.Path, null, null, ctx.RequestAborted);
                });
                app.MapPost("/{**path}", async ctx =>
                {
                    await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upPort, ctx.Request.Path, null, null, ctx.RequestAborted);
                });
            },
            "OPTIONS /Microsoft-Server-ActiveSync HTTP/1.1\r\n" +
            "Host: mail.marivil.com\r\n" +
            "Connection: close\r\n\r\n");

        // OPTIONS returns 404 because there's no MapOptions in CosmoApiServer
        Assert.Contains("HTTP/1.1 404", response);

        await forwarder.DisposeAsync();
        upstreamCts.Cancel();
        try { await upstreamTask; } catch { }
    }

    [Fact]
    public async Task ProxyPattern_ActiveSyncPost_WithAuth_ThroughProxy()
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
            var resp = "HTTP/1.1 200 OK\r\nContent-Type: application/vnd.ms-sync.wbxml\r\nContent-Length: 4\r\nConnection: close\r\n\r\ntest";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(resp), upstreamCts.Token);
        }, upstreamCts.Token);

        var forwarder = new PipelineHttpForwarder();
        var upPort = upstreamPort;

        var response = await RunFullPipelineRequest(
            app =>
            {
                RequestDelegate handler = async ctx =>
                {
                    var path = ctx.Request.Path + (string.IsNullOrEmpty(ctx.Request.QueryString) ? "" : "?" + ctx.Request.QueryString);
                    await forwarder.ForwardAsync(ctx, "http", "127.0.0.1", upPort, path, null, null, ctx.RequestAborted);
                };
                app.MapGet("/{**path}", handler);
                app.MapPost("/{**path}", handler);
            },
            "POST /Microsoft-Server-ActiveSync?Cmd=FolderSync&User=kutty@murshisoft.com&DeviceId=test HTTP/1.1\r\n" +
            "Host: mail.marivil.com\r\n" +
            "Authorization: Basic a3V0dHlAbXVyc2hpc29mdC5jb206UHZrcnVieTEK\r\n" +
            "Content-Type: application/vnd.ms-sync.wbxml\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n");

        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.NotNull(receivedAuth);
        Assert.StartsWith("Basic", receivedAuth);

        await forwarder.DisposeAsync();
        upstreamCts.Cancel();
        try { await upstreamTask; } catch { }
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static async Task<string> RunFullPipelineRequest(
        Action<CosmoWebApplication> configureApp,
        string rawRequest,
        int timeoutSeconds = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        using var portFinder = new TcpListener(IPAddress.Loopback, 0);
        portFinder.Start();
        int port = ((IPEndPoint)portFinder.LocalEndpoint).Port;
        portFinder.Stop();

        var builder = CosmoWebApplicationBuilder.Create().ListenOn(port);
        var app = builder.Build();
        configureApp(app);

        var serverTask = Task.Run(() => app.RunAsync(cts.Token), cts.Token);
        await Task.Delay(300, cts.Token);

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
