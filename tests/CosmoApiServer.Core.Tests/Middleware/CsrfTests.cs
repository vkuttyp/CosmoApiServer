using System.Text.Json;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class CsrfTests
{
    private static HttpContext MakeContext(HttpMethod method, Dictionary<string, string>? headers = null)
    {
        var req = new HttpRequest { Method = method, Path = "/test", Headers = headers ?? new Dictionary<string, string>() };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task Csrf_SafeMethod_SetsCookie()
    {
        var middleware = new CsrfMiddleware(new CsrfOptions());
        var ctx = MakeContext(HttpMethod.GET);

        await middleware.InvokeAsync(ctx, _ => Task.CompletedTask);

        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.True(ctx.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Contains("XSRF-TOKEN=", ctx.Response.Headers["Set-Cookie"]);
    }

    [Fact]
    public async Task Csrf_UnsafeMethod_BlocksWithoutToken()
    {
        var middleware = new CsrfMiddleware(new CsrfOptions());
        var ctx = MakeContext(HttpMethod.POST);

        await middleware.InvokeAsync(ctx, _ => Task.CompletedTask);

        Assert.Equal(403, ctx.Response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(ctx.Response.Body);
        Assert.Equal("CsrfValidationFailed", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Csrf_UnsafeMethod_AllowsWithValidToken()
    {
        var middleware = new CsrfMiddleware(new CsrfOptions());
        var token = "test-token";
        
        var headers = new Dictionary<string, string>
        {
            { "cookie", $"XSRF-TOKEN={token}" },
            { "X-XSRF-TOKEN", token }
        };
        var ctx = MakeContext(HttpMethod.POST, headers);
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);
        Assert.Equal(200, ctx.Response.StatusCode);
    }
}
