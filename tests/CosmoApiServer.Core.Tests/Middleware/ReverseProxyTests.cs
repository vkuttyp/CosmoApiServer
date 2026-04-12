using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class ReverseProxyTests
{
    private static HttpContext MakeContext(HttpMethod method = HttpMethod.GET, string path = "/")
    {
        var req = new HttpRequest { Method = method, Path = path };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task NoMatchingRoute_PassesThrough()
    {
        var options = new ReverseProxyOptions();
        options.Routes.Add(new ProxyRoute { PathPrefix = "/proxy", Destination = "http://127.0.0.1:9" });
        var middleware = new ReverseProxyMiddleware(options);
        var ctx = MakeContext(path: "/api/data");
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ExcludedPrefix_PassesThrough()
    {
        var options = new ReverseProxyOptions();
        options.Routes.Add(new ProxyRoute
        {
            PathPrefix       = "/",
            Destination      = "http://127.0.0.1:9",
            ExcludedPrefixes = ["/api"]
        });
        var middleware = new ReverseProxyMiddleware(options);
        var ctx = MakeContext(path: "/api/dashboard");
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task MatchingRoute_UpstreamUnreachable_Returns502()
    {
        var options = new ReverseProxyOptions();
        options.Routes.Add(new ProxyRoute { PathPrefix = "/", Destination = "http://127.0.0.1:9" });
        var middleware = new ReverseProxyMiddleware(options);
        var ctx = MakeContext(path: "/some/page");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(502, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task MatchingRoute_MatchedButExcluded_PassesThrough()
    {
        var options = new ReverseProxyOptions();
        options.Routes.Add(new ProxyRoute
        {
            PathPrefix       = "/",
            Destination      = "http://127.0.0.1:9",
            ExcludedPrefixes = ["/health", "/api"]
        });
        var middleware = new ReverseProxyMiddleware(options);

        foreach (var path in new[] { "/health", "/api/users", "/api" })
        {
            var ctx = MakeContext(path: path);
            var nextCalled = false;
            await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });
            Assert.True(nextCalled, $"Expected next() to be called for path: {path}");
        }
    }

    [Fact]
    public async Task WebSocketUpgrade_WithoutContextWebSocket_Returns501()
    {
        // The framework's transport must complete the WebSocket handshake and store
        // the socket in context.Items["WebSocket"]. Without it, the proxy returns 501.
        var options = new ReverseProxyOptions();
        options.Routes.Add(new ProxyRoute { PathPrefix = "/", Destination = "ws://127.0.0.1:9" });
        var middleware = new ReverseProxyMiddleware(options);
        var ctx = MakeContext(path: "/_vite/hmr");
        ctx.Request.Headers = new Dictionary<string, string> { ["Upgrade"] = "websocket" };
        // context.Items["WebSocket"] is deliberately NOT set

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(501, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task RouteSelection_FirstMatchWins()
    {
        // Only one route should match; the second should not be tried.
        var options = new ReverseProxyOptions();
        options.Routes.Add(new ProxyRoute { PathPrefix = "/specific", Destination = "http://127.0.0.1:9" });
        options.Routes.Add(new ProxyRoute { PathPrefix = "/",         Destination = "http://127.0.0.1:8" });
        var middleware = new ReverseProxyMiddleware(options);

        // /specific matches the first route → 502 from port 9 (not 8)
        var ctx = MakeContext(path: "/specific/page");
        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);
        Assert.Equal(502, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task EmptyRouteList_AlwaysPassesThrough()
    {
        var middleware = new ReverseProxyMiddleware(new ReverseProxyOptions());
        var ctx = MakeContext(path: "/anything");
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });

        Assert.True(nextCalled);
    }
}
