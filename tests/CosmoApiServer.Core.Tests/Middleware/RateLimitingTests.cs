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
}
