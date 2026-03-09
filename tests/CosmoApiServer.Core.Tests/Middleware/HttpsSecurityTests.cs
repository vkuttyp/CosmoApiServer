using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class HttpsSecurityTests
{
    private static HttpContext MakeContext(bool isHttps, string host = "localhost")
    {
        var req = new HttpRequest 
        { 
            Method = HttpMethod.GET, 
            Path = "/test", 
            QueryString = "?a=b",
            Headers = new Dictionary<string, string> { { "host", host } }
        };
        var res = new HttpResponse();
        var ctx = new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
        if (isHttps) ctx.Items["__IsHttps"] = true;
        return ctx;
    }

    [Fact]
    public async Task HttpsRedirection_RedirectsHttpToHttps()
    {
        var middleware = new HttpsRedirectionMiddleware(new HttpsRedirectionOptions());
        var ctx = MakeContext(false, "example.com:8080");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(307, ctx.Response.StatusCode);
        Assert.Equal("https://example.com/test?a=b", ctx.Response.Headers["Location"]);
    }

    [Fact]
    public async Task HttpsRedirection_PassesThroughWhenHttps()
    {
        var middleware = new HttpsRedirectionMiddleware(new HttpsRedirectionOptions());
        var ctx = MakeContext(true);
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });

        Assert.True(nextCalled);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Hsts_SetsHeaderOnHttps()
    {
        var middleware = new HstsMiddleware(new HstsOptions());
        var ctx = MakeContext(true);

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.True(ctx.Response.Headers.ContainsKey("Strict-Transport-Security"));
        Assert.Contains("max-age=31536000", ctx.Response.Headers["Strict-Transport-Security"]);
    }

    [Fact]
    public async Task Hsts_DoesNotSetHeaderOnHttp()
    {
        var middleware = new HstsMiddleware(new HstsOptions());
        var ctx = MakeContext(false);

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.False(ctx.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }
}
