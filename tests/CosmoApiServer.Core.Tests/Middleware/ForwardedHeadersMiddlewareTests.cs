using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class ForwardedHeadersMiddlewareTests
{
    private static HttpContext MakeContext(
        string? remoteIp = null,
        Dictionary<string, string>? headers = null)
    {
        var req = new HttpRequest
        {
            Method = HttpMethod.GET,
            Path = "/test",
            Headers = headers ?? new Dictionary<string, string>()
        };
        var res = new HttpResponse();
        var ctx = new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
        if (remoteIp is not null)
            ctx.Items["RemoteIpAddress"] = remoteIp;
        return ctx;
    }

    // ── Safe-default tests ────────────────────────────────────────────────────

    [Fact]
    public void DefaultOptions_ForwardedHeaders_IsNone()
    {
        // Regression: the default was ForwardedHeaders.All, which trusted any proxy.
        // It is now None — callers must explicitly opt in.
        var options = new ForwardedHeadersOptions();
        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
    }

    [Fact]
    public async Task NoKnownProxies_HeadersAreIgnored_EvenWhenPresent()
    {
        // When KnownProxies and KnownNetworks are both empty, no forwarded headers
        // should be applied — regardless of what the client sends.
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.All
            // KnownProxies and KnownNetworks intentionally left empty
        };
        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "203.0.113.1",
            headers: new Dictionary<string, string>
            {
                ["X-Forwarded-For"] = "10.0.0.99",
                ["X-Forwarded-Proto"] = "https",
                ["X-Forwarded-Host"] = "evil.example.com"
            });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.False(ctx.Items.ContainsKey("X-Forwarded-For"));
        Assert.False(ctx.Items.ContainsKey("X-Forwarded-Proto"));
        Assert.False(ctx.Items.ContainsKey("X-Forwarded-Host"));
    }

    // ── Trusted proxy — exact IP ──────────────────────────────────────────────

    [Fact]
    public async Task TrustedProxyIp_XForwardedFor_SetsClientIp()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
        };
        options.KnownProxies.Add("10.0.0.1");

        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "10.0.0.1",
            headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.42" });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal("203.0.113.42", ctx.Items["X-Forwarded-For"]);
    }

    [Fact]
    public async Task TrustedProxyIp_XForwardedProto_SetsProto()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto
        };
        options.KnownProxies.Add("10.0.0.1");

        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "10.0.0.1",
            headers: new Dictionary<string, string> { ["X-Forwarded-Proto"] = "https" });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal("https", ctx.Items["X-Forwarded-Proto"]);
    }

    [Fact]
    public async Task TrustedProxyIp_XForwardedHost_SetsHost()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedHost
        };
        options.KnownProxies.Add("10.0.0.1");

        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "10.0.0.1",
            headers: new Dictionary<string, string> { ["X-Forwarded-Host"] = "app.example.com" });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal("app.example.com", ctx.Items["X-Forwarded-Host"]);
    }

    // ── Untrusted proxy ───────────────────────────────────────────────────────

    [Fact]
    public async Task UntrustedProxyIp_HeadersAreIgnored()
    {
        // Regression: previously all proxies were trusted regardless of KnownProxies.
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.All
        };
        options.KnownProxies.Add("10.0.0.1"); // only this IP is trusted

        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "203.0.113.99", // different IP — untrusted
            headers: new Dictionary<string, string>
            {
                ["X-Forwarded-For"] = "10.0.0.50",
                ["X-Forwarded-Proto"] = "https",
                ["X-Forwarded-Host"] = "attacker.example.com"
            });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.False(ctx.Items.ContainsKey("X-Forwarded-For"));
        Assert.False(ctx.Items.ContainsKey("X-Forwarded-Proto"));
        Assert.False(ctx.Items.ContainsKey("X-Forwarded-Host"));
    }

    // ── CIDR network matching ─────────────────────────────────────────────────

    [Fact]
    public async Task KnownNetwork_CidrMatch_TrustsProxy()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
        };
        options.KnownNetworks.Add("10.0.0.0/8"); // entire 10.x.x.x range

        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "10.42.7.3", // inside the /8 network
            headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "192.168.1.100" });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal("192.168.1.100", ctx.Items["X-Forwarded-For"]);
    }

    [Fact]
    public async Task KnownNetwork_CidrMismatch_DoesNotTrustProxy()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
        };
        options.KnownNetworks.Add("10.0.0.0/8");

        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "172.16.0.1", // outside the /8 network
            headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "1.2.3.4" });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.False(ctx.Items.ContainsKey("X-Forwarded-For"));
    }

    [Fact]
    public async Task KnownNetwork_SlashTwentyFour_MatchesCorrectly()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
        };
        options.KnownNetworks.Add("192.168.1.0/24");

        var middleware = new ForwardedHeadersMiddleware(options);

        // Inside the /24
        var ctxIn = MakeContext(
            remoteIp: "192.168.1.50",
            headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "8.8.8.8" });
        await middleware.InvokeAsync(ctxIn, _ => ValueTask.CompletedTask);
        Assert.Equal("8.8.8.8", ctxIn.Items["X-Forwarded-For"]);

        // Outside the /24 (different third octet)
        var ctxOut = MakeContext(
            remoteIp: "192.168.2.50",
            headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "8.8.8.8" });
        await middleware.InvokeAsync(ctxOut, _ => ValueTask.CompletedTask);
        Assert.False(ctxOut.Items.ContainsKey("X-Forwarded-For"));
    }

    // ── ForwardLimit ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ForwardLimit_SelectsCorrectEntryFromMultiHopChain()
    {
        // X-Forwarded-For: client, proxy1, proxy2
        // ForwardLimit=1 → read the entry written by the rightmost (closest) trusted proxy.
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1
        };
        options.KnownProxies.Add("10.0.0.1");

        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "10.0.0.1",
            headers: new Dictionary<string, string>
            {
                ["X-Forwarded-For"] = "203.0.113.5, 10.0.0.50, 10.0.0.2"
            });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        // With ForwardLimit=1 we take addresses[^1] = "10.0.0.2"
        Assert.Equal("10.0.0.2", ctx.Items["X-Forwarded-For"]);
    }

    // ── Selective header flags ────────────────────────────────────────────────

    [Fact]
    public async Task FlagNone_NoHeadersProcessed_EvenFromTrustedProxy()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.None // explicit no-op
        };
        options.KnownProxies.Add("10.0.0.1");

        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "10.0.0.1",
            headers: new Dictionary<string, string>
            {
                ["X-Forwarded-For"] = "1.2.3.4",
                ["X-Forwarded-Proto"] = "https"
            });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.False(ctx.Items.ContainsKey("X-Forwarded-For"));
        Assert.False(ctx.Items.ContainsKey("X-Forwarded-Proto"));
    }

    [Fact]
    public async Task FlagXForwardedFor_OnlyProcessesFor_NotProtoOrHost()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
        };
        options.KnownProxies.Add("10.0.0.1");

        var middleware = new ForwardedHeadersMiddleware(options);
        var ctx = MakeContext(
            remoteIp: "10.0.0.1",
            headers: new Dictionary<string, string>
            {
                ["X-Forwarded-For"] = "203.0.113.5",
                ["X-Forwarded-Proto"] = "https",
                ["X-Forwarded-Host"] = "app.example.com"
            });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal("203.0.113.5", ctx.Items["X-Forwarded-For"]);
        Assert.False(ctx.Items.ContainsKey("X-Forwarded-Proto"));
        Assert.False(ctx.Items.ContainsKey("X-Forwarded-Host"));
    }
}
