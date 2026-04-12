using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class CorsMiddlewareTests
{
    private static HttpContext MakeContext(
        HttpMethod method = HttpMethod.GET,
        string? origin = null,
        string path = "/api/data")
    {
        var headers = new Dictionary<string, string>();
        if (origin is not null)
            headers["Origin"] = origin;

        var req = new HttpRequest { Method = method, Path = path, Headers = headers };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task Cors_Preflight_AllowedOrigin_Returns204WithCorsHeaders()
    {
        var options = new CorsOptions { AllowedOrigins = ["https://allowed.com"] };
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.OPTIONS, "https://allowed.com");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(204, ctx.Response.StatusCode);
        Assert.Equal("https://allowed.com", ctx.Response.Headers["Access-Control-Allow-Origin"]);
    }

    [Fact]
    public async Task Cors_Preflight_DeniedOrigin_Returns403()
    {
        var options = new CorsOptions { AllowedOrigins = ["https://allowed.com"] };
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.OPTIONS, "https://evil.com");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(403, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Cors_Preflight_DeniedOrigin_DoesNotSetAccessControlHeaders()
    {
        var options = new CorsOptions { AllowedOrigins = ["https://allowed.com"] };
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.OPTIONS, "https://evil.com");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.DoesNotContain("Access-Control-Allow-Origin", ctx.Response.Headers);
        Assert.DoesNotContain("Access-Control-Allow-Methods", ctx.Response.Headers);
    }

    [Fact]
    public async Task Cors_NonPreflight_DeniedOrigin_DoesNotSetAccessControlHeaders()
    {
        var options = new CorsOptions { AllowedOrigins = ["https://allowed.com"] };
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.GET, "https://evil.com");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.DoesNotContain("Access-Control-Allow-Origin", ctx.Response.Headers);
    }

    [Fact]
    public async Task Cors_NonPreflight_AllowedOrigin_SetsAccessControlHeader()
    {
        var options = new CorsOptions { AllowedOrigins = ["https://allowed.com"] };
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.GET, "https://allowed.com");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal("https://allowed.com", ctx.Response.Headers["Access-Control-Allow-Origin"]);
    }

    [Fact]
    public async Task Cors_WildcardOrigin_AllowsAnyOrigin()
    {
        var options = new CorsOptions();
        options.AllowAnyOrigin();
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.GET, "https://random.example.com");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal("https://random.example.com", ctx.Response.Headers["Access-Control-Allow-Origin"]);
    }

    [Fact]
    public async Task Cors_NoOriginHeader_NextIsCalled()
    {
        var options = new CorsOptions { AllowedOrigins = ["https://allowed.com"] };
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.GET, origin: null);

        bool nextCalled = false;
        await middleware.InvokeAsync(ctx, _ =>
        {
            nextCalled = true;
            return ValueTask.CompletedTask;
        });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Cors_NoOriginHeader_DoesNotInjectAnyCorsHeaders()
    {
        // Regression: previously a missing Origin header was synthesised as "*", which
        // caused CORS headers to be written on non-browser (server-to-server) requests.
        var options = new CorsOptions();
        options.AllowAnyOrigin();
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.GET, origin: null);

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.DoesNotContain("Access-Control-Allow-Origin", ctx.Response.Headers);
    }

    [Fact]
    public async Task Cors_SpoofedWildcardOrigin_IsBlockedWhenSpecificOriginsConfigured()
    {
        // Regression: a client sending "Origin: *" could previously match the internal
        // _allowAll sentinel, bypassing an explicit allowlist.
        var options = new CorsOptions { AllowedOrigins = ["https://allowed.com"] };
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.OPTIONS, origin: "*");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(403, ctx.Response.StatusCode);
        Assert.DoesNotContain("Access-Control-Allow-Origin", ctx.Response.Headers);
    }

    [Fact]
    public async Task Cors_NonPreflight_DoesNotSendAllowMethodsOrAllowHeaders()
    {
        // Regression: Access-Control-Allow-Methods and Access-Control-Allow-Headers are
        // preflight-only per the CORS spec and were incorrectly sent on every response.
        var options = new CorsOptions { AllowedOrigins = ["https://allowed.com"] };
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.GET, "https://allowed.com");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.DoesNotContain("Access-Control-Allow-Methods", ctx.Response.Headers);
        Assert.DoesNotContain("Access-Control-Allow-Headers", ctx.Response.Headers);
    }

    [Fact]
    public async Task Cors_Preflight_AllowedOrigin_DoesIncludeAllowMethodsAndHeaders()
    {
        // Preflight responses must still include Allow-Methods and Allow-Headers.
        var options = new CorsOptions { AllowedOrigins = ["https://allowed.com"] };
        var middleware = new CorsMiddleware(options);
        var ctx = MakeContext(HttpMethod.OPTIONS, "https://allowed.com");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(204, ctx.Response.StatusCode);
        Assert.Contains("Access-Control-Allow-Methods", ctx.Response.Headers);
        Assert.Contains("Access-Control-Allow-Headers", ctx.Response.Headers);
    }
}
