using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class ViteDevProxyTests
{
    private static HttpContext MakeContext(HttpMethod method = HttpMethod.GET, string path = "/test",
        Dictionary<string, string>? headers = null)
    {
        var req = new HttpRequest { Method = method, Path = path, Headers = headers ?? new Dictionary<string, string>() };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task NonProxiedPath_PassesThrough()
    {
        var options = new ViteDevProxyOptions { DevServerUrl = "http://127.0.0.1:9999" };
        var middleware = new ViteDevProxyMiddleware(options);
        var ctx = MakeContext(path: "/api/data");
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/@vite/client")]
    [InlineData("/@fs/some/path/file.ts")]
    [InlineData("/_nuxt/builds/meta.json")]
    [InlineData("/__nuxt/error.vue")]
    [InlineData("/@id/__nuxt_css")]
    public async Task ProxiedPath_DoesNotCallNext(string path)
    {
        // The proxy will attempt a real HTTP connection and fail (no server running),
        // returning 502. What matters is that next() is NOT called.
        var options = new ViteDevProxyOptions { DevServerUrl = "http://127.0.0.1:9" };
        var middleware = new ViteDevProxyMiddleware(options);
        var ctx = MakeContext(path: path);
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task WebSocketUpgrade_Returns501()
    {
        var options = new ViteDevProxyOptions { DevServerUrl = "http://127.0.0.1:9999" };
        var middleware = new ViteDevProxyMiddleware(options);
        var ctx = MakeContext(
            path: "/@vite/hmr",
            headers: new Dictionary<string, string> { ["Upgrade"] = "websocket" });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(501, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task DevServerUnreachable_Returns502()
    {
        // Port 9 is the discard protocol — connections are refused immediately.
        var options = new ViteDevProxyOptions { DevServerUrl = "http://127.0.0.1:9" };
        var middleware = new ViteDevProxyMiddleware(options);
        var ctx = MakeContext(path: "/@vite/client");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(502, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task CustomProxiedPrefixes_AreRespected()
    {
        var options = new ViteDevProxyOptions
        {
            DevServerUrl     = "http://127.0.0.1:9",
            ProxiedPrefixes  = ["/custom-prefix"]
        };
        var middleware = new ViteDevProxyMiddleware(options);

        // Default prefix /@vite is not in the custom list — should pass through.
        var ctx1 = MakeContext(path: "/@vite/client");
        var next1Called = false;
        await middleware.InvokeAsync(ctx1, _ => { next1Called = true; return ValueTask.CompletedTask; });
        Assert.True(next1Called);

        // Custom prefix should be proxied.
        var ctx2 = MakeContext(path: "/custom-prefix/module.js");
        var next2Called = false;
        await middleware.InvokeAsync(ctx2, _ => { next2Called = true; return ValueTask.CompletedTask; });
        Assert.False(next2Called);
        Assert.Equal(502, ctx2.Response.StatusCode);
    }
}
