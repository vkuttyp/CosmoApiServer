using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class OutputCacheTests
{
    private static HttpContext MakeContext(string path = "/test", HttpMethod method = HttpMethod.GET)
    {
        var req = new HttpRequest { Method = method, Path = path };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    private static OutputCachingMiddleware MakeMiddleware(out InMemoryOutputCacheStore store)
    {
        store = new InMemoryOutputCacheStore();
        return new OutputCachingMiddleware(store, new OutputCacheOptions { DefaultExpiry = TimeSpan.FromMinutes(1) });
    }

    [Fact]
    public async Task OutputCache_FirstRequest_IsMiss_BodyCaptured()
    {
        var middleware = MakeMiddleware(out var store);
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, c =>
        {
            c.Response.StatusCode = 200;
            c.Response.Write("hello"u8.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Equal("MISS", ctx.Response.Headers["X-Output-Cache"]);
        var entry = await store.GetAsync("GET:/test", CancellationToken.None);
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task OutputCache_SecondRequest_IsHit()
    {
        var middleware = MakeMiddleware(out _);
        var ctx1 = MakeContext();

        await middleware.InvokeAsync(ctx1, c =>
        {
            c.Response.StatusCode = 200;
            c.Response.Write("cached-body"u8.ToArray());
            return ValueTask.CompletedTask;
        });

        var ctx2 = MakeContext();
        int nextCallCount = 0;
        await middleware.InvokeAsync(ctx2, _ =>
        {
            nextCallCount++;
            return ValueTask.CompletedTask;
        });

        Assert.Equal("HIT", ctx2.Response.Headers["X-Output-Cache"]);
        Assert.Equal(0, nextCallCount); // next not called on cache hit
        Assert.Equal("cached-body", System.Text.Encoding.UTF8.GetString(ctx2.Response.Body));
    }

    [Fact]
    public async Task OutputCache_PostRequest_NotCached()
    {
        var middleware = MakeMiddleware(out var store);
        var ctx = MakeContext(method: HttpMethod.POST);

        await middleware.InvokeAsync(ctx, c =>
        {
            c.Response.StatusCode = 200;
            c.Response.Write("body"u8.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.False(ctx.Response.Headers.ContainsKey("X-Output-Cache"));
    }

    [Fact]
    public async Task OutputCache_NoCacheHeader_Bypasses()
    {
        var middleware = MakeMiddleware(out _);
        // Seed the cache first
        var ctx1 = MakeContext();
        await middleware.InvokeAsync(ctx1, c =>
        {
            c.Response.StatusCode = 200;
            c.Response.Write("old"u8.ToArray());
            return ValueTask.CompletedTask;
        });

        // Second request with no-cache should bypass cache
        var ctx2 = new HttpContext(
            new HttpRequest { Method = HttpMethod.GET, Path = "/test", Headers = new Dictionary<string, string> { ["Cache-Control"] = "no-cache" } },
            new HttpResponse(),
            new ServiceCollection().BuildServiceProvider());
        int nextCalled = 0;
        await middleware.InvokeAsync(ctx2, _ =>
        {
            nextCalled++;
            return ValueTask.CompletedTask;
        });

        Assert.Equal(1, nextCalled);
        Assert.False(ctx2.Response.Headers.ContainsKey("X-Output-Cache"));
    }

    [Fact]
    public async Task OutputCache_TagEviction_RemovesEntry()
    {
        var store = new InMemoryOutputCacheStore();
        var policy = OutputCachePolicy.Build().Tag("products").ToPolicy();
        var middleware = new OutputCachingMiddleware(store, new OutputCacheOptions());

        var ctx = MakeContext("/products");
        ctx.SetOutputCachePolicy(policy);
        await middleware.InvokeAsync(ctx, c =>
        {
            c.Response.StatusCode = 200;
            c.Response.Write("products"u8.ToArray());
            return ValueTask.CompletedTask;
        });

        // Entry should exist
        Assert.NotNull(await store.GetAsync("GET:/products", CancellationToken.None));

        // Evict by tag
        await store.EvictByTagAsync("products");

        // Entry should be gone
        Assert.Null(await store.GetAsync("GET:/products", CancellationToken.None));
    }

    [Fact]
    public async Task OutputCache_VaryByQuery_DifferentKeysForDifferentParams()
    {
        var store = new InMemoryOutputCacheStore();
        var policy = OutputCachePolicy.Build().VaryByQuery("page").ToPolicy();
        var middleware = new OutputCachingMiddleware(store, new OutputCacheOptions());

        var ctx1 = MakeContext("/items?page=1");
        ctx1.Request.QueryString = "?page=1";
        ctx1.SetOutputCachePolicy(policy);
        await middleware.InvokeAsync(ctx1, c =>
        {
            c.Response.StatusCode = 200;
            c.Response.Write("page1"u8.ToArray());
            return ValueTask.CompletedTask;
        });

        var ctx2 = MakeContext("/items?page=2");
        ctx2.Request.QueryString = "?page=2";
        ctx2.SetOutputCachePolicy(policy);
        await middleware.InvokeAsync(ctx2, c =>
        {
            c.Response.StatusCode = 200;
            c.Response.Write("page2"u8.ToArray());
            return ValueTask.CompletedTask;
        });

        // Both should be MISS (different cache keys)
        Assert.Equal("MISS", ctx1.Response.Headers["X-Output-Cache"]);
        Assert.Equal("MISS", ctx2.Response.Headers["X-Output-Cache"]);
    }
}
