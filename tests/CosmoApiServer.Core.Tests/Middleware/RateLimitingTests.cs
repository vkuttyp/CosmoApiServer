using System.Text.Json;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class RateLimitingTests
{
    private static HttpContext MakeContext()
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/test" };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task RateLimiting_AllowsRequestsWithinLimit()
    {
        var options = new RateLimitOptions { Limit = 2, Window = TimeSpan.FromSeconds(10) };
        var middleware = new RateLimitingMiddleware(options);
        var ctx = MakeContext();

        // 1st request
        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);
        Assert.Equal(200, ctx.Response.StatusCode);

        // 2nd request
        ctx = MakeContext();
        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task RateLimiting_BlocksRequestsExceedingLimit()
    {
        var options = new RateLimitOptions { Limit = 1, Window = TimeSpan.FromSeconds(10) };
        var middleware = new RateLimitingMiddleware(options);
        
        // 1st request - OK
        var ctx1 = MakeContext();
        await middleware.InvokeAsync(ctx1, _ => ValueTask.CompletedTask);
        Assert.Equal(200, ctx1.Response.StatusCode);

        // 2nd request - Blocked
        var ctx2 = MakeContext();
        await middleware.InvokeAsync(ctx2, _ => ValueTask.CompletedTask);
        Assert.Equal(429, ctx2.Response.StatusCode);
        Assert.True(ctx2.Response.Headers.ContainsKey("Retry-After"));
        
        var body = JsonSerializer.Deserialize<JsonElement>(ctx2.Response.Body);
        Assert.Equal("RateLimitExceeded", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RateLimiting_ResetsAfterWindow()
    {
        var options = new RateLimitOptions { Limit = 1, Window = TimeSpan.FromMilliseconds(100) };
        var middleware = new RateLimitingMiddleware(options);
        
        // 1st request - OK
        var ctx1 = MakeContext();
        await middleware.InvokeAsync(ctx1, _ => ValueTask.CompletedTask);
        Assert.Equal(200, ctx1.Response.StatusCode);

        // Wait for window to expire
        await Task.Delay(150);

        // 2nd request - OK again
        var ctx2 = MakeContext();
        await middleware.InvokeAsync(ctx2, _ => ValueTask.CompletedTask);
        Assert.Equal(200, ctx2.Response.StatusCode);
    }

    [Fact]
    public async Task RateLimiting_RetryAfter_IsAlwaysAtLeastOne()
    {
        // Regression: the Retry-After value could be zero or negative when the rate-limit
        // window expired between the count check and the header write. Math.Max(1, ...) now
        // floors the value.
        var options = new RateLimitOptions { Limit = 1, Window = TimeSpan.FromMilliseconds(1) };
        var middleware = new RateLimitingMiddleware(options);

        // Burn the limit
        var ctx1 = MakeContext();
        await middleware.InvokeAsync(ctx1, _ => ValueTask.CompletedTask);

        // Wait for the window to expire so WindowEnd ≈ now
        await Task.Delay(10);

        // Next request is blocked — window has expired but the entry may not have reset yet
        var ctx2 = MakeContext();
        await middleware.InvokeAsync(ctx2, _ => ValueTask.CompletedTask);

        if (ctx2.Response.StatusCode == 429)
        {
            Assert.True(ctx2.Response.Headers.ContainsKey("Retry-After"));
            var retryAfter = int.Parse(ctx2.Response.Headers["Retry-After"]);
            Assert.True(retryAfter >= 1, $"Retry-After must be >= 1, got {retryAfter}");
        }
        // If the window reset cleanly and the request was allowed (200), that's also valid.
    }

    [Fact]
    public async Task RateLimiting_ConcurrentRequests_NeverExceedConfiguredLimit()
    {
        const int limit = 5;
        const int total = 40;
        var options = new RateLimitOptions { Limit = limit, Window = TimeSpan.FromSeconds(30) };
        var middleware = new RateLimitingMiddleware(options);

        int successCount = 0;
        int limitedCount = 0;

        // Fire all requests concurrently — TOCTOU fix must ensure exactly `limit` succeed
        await Parallel.ForEachAsync(
            Enumerable.Range(0, total),
            new ParallelOptions { MaxDegreeOfParallelism = total },
            async (_, _) =>
            {
                var ctx = MakeContext();
                await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);
                if (ctx.Response.StatusCode == 200)
                    Interlocked.Increment(ref successCount);
                else
                    Interlocked.Increment(ref limitedCount);
            });

        Assert.Equal(limit, successCount);
        Assert.Equal(total - limit, limitedCount);
    }
}
