using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class AntiforgeryTests
{
    private static (HttpContext ctx, IAntiforgeryService svc) MakeContext(HttpMethod method = HttpMethod.GET)
    {
        var opts = new AntiforgeryOptions();
        var svc = new DefaultAntiforgeryService(opts);
        var req = new HttpRequest { Method = method, Path = "/test" };
        var res = new HttpResponse();
        var services = new ServiceCollection();
        services.AddSingleton(opts);
        services.AddSingleton<IAntiforgeryService>(svc);
        var ctx = new HttpContext(req, res, services.BuildServiceProvider());
        return (ctx, svc);
    }

    [Fact]
    public void GetAndStoreTokens_ReturnsCookieAndRequestToken()
    {
        var (ctx, svc) = MakeContext();

        var tokens = svc.GetAndStoreTokens(ctx);

        Assert.False(string.IsNullOrEmpty(tokens.CookieToken));
        Assert.False(string.IsNullOrEmpty(tokens.RequestToken));
        Assert.NotEqual(tokens.CookieToken, tokens.RequestToken);
        Assert.Contains("Set-Cookie", ctx.Response.Headers.Keys);
    }

    [Fact]
    public void GetRequest_IsAlwaysValid()
    {
        var (ctx, svc) = MakeContext(HttpMethod.GET);

        Assert.True(svc.IsRequestValid(ctx));
    }

    [Fact]
    public void PostRequest_WithoutToken_IsInvalid()
    {
        var (ctx, svc) = MakeContext(HttpMethod.POST);

        Assert.False(svc.IsRequestValid(ctx));
    }

    [Fact]
    public void PostRequest_WithValidHeaderToken_IsValid()
    {
        var opts = new AntiforgeryOptions();
        var (getCtx, svc) = MakeContext(HttpMethod.GET);
        var tokens = svc.GetAndStoreTokens(getCtx);

        var headers = new Dictionary<string, string>
        {
            ["Cookie"] = $"{opts.CookieName}={tokens.CookieToken}",
            [opts.HeaderName] = tokens.RequestToken
        };
        var req = new HttpRequest { Method = HttpMethod.POST, Path = "/test", Headers = headers };
        var postCtx = new HttpContext(req, new HttpResponse(), new ServiceCollection().BuildServiceProvider());

        Assert.True(svc.IsRequestValid(postCtx));
    }

    [Fact]
    public async Task AntiforgeryMiddleware_BlocksPost_WithoutToken()
    {
        var (ctx, svc) = MakeContext(HttpMethod.POST);
        var middleware = new AntiforgeryMiddleware(svc);

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(400, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task AntiforgeryMiddleware_AllowsGet_Always()
    {
        var (ctx, svc) = MakeContext(HttpMethod.GET);
        var middleware = new AntiforgeryMiddleware(svc);
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });

        Assert.True(nextCalled);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact]
    public void ValidateRequest_ThrowsOnInvalidToken()
    {
        var (ctx, svc) = MakeContext(HttpMethod.POST);

        Assert.Throws<InvalidOperationException>(() => svc.ValidateRequest(ctx));
    }

    [Fact]
    public void PostRequest_WithTamperedToken_IsInvalid()
    {
        var opts = new AntiforgeryOptions();
        var (getCtx, svc) = MakeContext(HttpMethod.GET);
        var tokens = svc.GetAndStoreTokens(getCtx);

        var headers = new Dictionary<string, string>
        {
            ["Cookie"] = $"{opts.CookieName}={tokens.CookieToken}",
            [opts.HeaderName] = "tampered-token-value"
        };
        var req = new HttpRequest { Method = HttpMethod.POST, Path = "/test", Headers = headers };
        var postCtx = new HttpContext(req, new HttpResponse(), new ServiceCollection().BuildServiceProvider());

        Assert.False(svc.IsRequestValid(postCtx));
    }
}
