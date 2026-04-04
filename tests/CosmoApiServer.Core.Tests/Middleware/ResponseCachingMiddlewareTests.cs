using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class ResponseCachingMiddlewareTests
{
    private static HttpContext MakeContext(string path = "/test", string? queryString = null, HttpMethod method = HttpMethod.GET)
    {
        var req = new HttpRequest { Method = method, Path = path, QueryString = queryString ?? string.Empty };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    private static ValueTask CachingHandler(HttpContext ctx)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.Headers["Cache-Control"] = "max-age=60";
        ctx.Response.WriteText("cached body");
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ResponseCaching_FirstRequest_CallsHandler()
    {
        var middleware = new ResponseCachingMiddleware(new ResponseCachingOptions());
        int callCount = 0;

        var ctx = MakeContext();
        await middleware.InvokeAsync(ctx, _ =>
        {
            callCount++;
            return CachingHandler(ctx);
        });

        Assert.Equal(1, callCount);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task ResponseCaching_SecondIdenticalRequest_UsesCache()
    {
        var middleware = new ResponseCachingMiddleware(new ResponseCachingOptions());
        int callCount = 0;

        ValueTask Handler(HttpContext c) { callCount++; return CachingHandler(c); }

        var ctx1 = MakeContext();
        await middleware.InvokeAsync(ctx1, Handler);
        Assert.Equal(1, callCount);

        var ctx2 = MakeContext();
        await middleware.InvokeAsync(ctx2, Handler);
        Assert.Equal(1, callCount); // handler not called again
        Assert.Contains("cached body", System.Text.Encoding.UTF8.GetString(ctx2.Response.Body));
    }

    [Fact]
    public async Task ResponseCaching_QueryParamCanonicalization_DifferentOrderHitsSameCache()
    {
        var middleware = new ResponseCachingMiddleware(new ResponseCachingOptions());
        int callCount = 0;

        ValueTask Handler(HttpContext c) { callCount++; return CachingHandler(c); }

        // Prime the cache with ?b=2&a=1
        var ctx1 = MakeContext(path: "/search", queryString: "b=2&a=1");
        await middleware.InvokeAsync(ctx1, Handler);
        Assert.Equal(1, callCount);

        // Same params, different order — must hit the same cache entry
        var ctx2 = MakeContext(path: "/search", queryString: "a=1&b=2");
        await middleware.InvokeAsync(ctx2, Handler);
        Assert.Equal(1, callCount); // handler NOT called again
        Assert.Contains("cached body", System.Text.Encoding.UTF8.GetString(ctx2.Response.Body));
    }

    [Fact]
    public async Task ResponseCaching_PostRequest_EvictsGetCacheForSamePath()
    {
        var middleware = new ResponseCachingMiddleware(new ResponseCachingOptions());
        int callCount = 0;

        ValueTask Handler(HttpContext c) { callCount++; return CachingHandler(c); }

        // Prime cache with GET /items
        var getCtx1 = MakeContext(path: "/items");
        await middleware.InvokeAsync(getCtx1, Handler);
        Assert.Equal(1, callCount);

        // Second GET hits cache
        var getCtx2 = MakeContext(path: "/items");
        await middleware.InvokeAsync(getCtx2, Handler);
        Assert.Equal(1, callCount);

        // POST to same path evicts the cache
        var postCtx = MakeContext(path: "/items", method: HttpMethod.POST);
        await middleware.InvokeAsync(postCtx, _ => ValueTask.CompletedTask);

        // Next GET should call the handler again (cache was evicted)
        var getCtx3 = MakeContext(path: "/items");
        await middleware.InvokeAsync(getCtx3, Handler);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ResponseCaching_PutRequest_EvictsGetCacheForSamePath()
    {
        var middleware = new ResponseCachingMiddleware(new ResponseCachingOptions());
        int callCount = 0;

        ValueTask Handler(HttpContext c) { callCount++; return CachingHandler(c); }

        var getCtx1 = MakeContext(path: "/data");
        await middleware.InvokeAsync(getCtx1, Handler);
        Assert.Equal(1, callCount);

        var putCtx = MakeContext(path: "/data", method: HttpMethod.PUT);
        await middleware.InvokeAsync(putCtx, _ => ValueTask.CompletedTask);

        var getCtx2 = MakeContext(path: "/data");
        await middleware.InvokeAsync(getCtx2, Handler);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ResponseCaching_DeleteRequest_EvictsGetCacheForSamePath()
    {
        var middleware = new ResponseCachingMiddleware(new ResponseCachingOptions());
        int callCount = 0;

        ValueTask Handler(HttpContext c) { callCount++; return CachingHandler(c); }

        var getCtx = MakeContext(path: "/resource");
        await middleware.InvokeAsync(getCtx, Handler);
        Assert.Equal(1, callCount);

        var deleteCtx = MakeContext(path: "/resource", method: HttpMethod.DELETE);
        await middleware.InvokeAsync(deleteCtx, _ => ValueTask.CompletedTask);

        var getCtx2 = MakeContext(path: "/resource");
        await middleware.InvokeAsync(getCtx2, Handler);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ResponseCaching_PostToDifferentPath_DoesNotEvictOtherPaths()
    {
        var middleware = new ResponseCachingMiddleware(new ResponseCachingOptions());
        int callCount = 0;

        ValueTask Handler(HttpContext c) { callCount++; return CachingHandler(c); }

        // Cache GET /widgets
        var getCtx = MakeContext(path: "/widgets");
        await middleware.InvokeAsync(getCtx, Handler);
        Assert.Equal(1, callCount);

        // POST to /other should not evict /widgets cache
        var postCtx = MakeContext(path: "/other", method: HttpMethod.POST);
        await middleware.InvokeAsync(postCtx, _ => ValueTask.CompletedTask);

        // GET /widgets still cached
        var getCtx2 = MakeContext(path: "/widgets");
        await middleware.InvokeAsync(getCtx2, Handler);
        Assert.Equal(1, callCount);
    }
}
