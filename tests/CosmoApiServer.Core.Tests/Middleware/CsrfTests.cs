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

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.True(ctx.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Contains("XSRF-TOKEN=", ctx.Response.Headers["Set-Cookie"]);
    }

    [Fact]
    public async Task Csrf_UnsafeMethod_BlocksWithoutToken()
    {
        var middleware = new CsrfMiddleware(new CsrfOptions());
        var ctx = MakeContext(HttpMethod.POST);

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

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
            return ValueTask.CompletedTask;
        });

        Assert.True(nextCalled);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Csrf_Post_To_Echo_IsNowBlocked()
    {
        // Regression: /echo was previously hardcoded as a CSRF bypass for benchmarking.
        // That bypass has been removed — POST /echo must go through validation like any
        // other state-changing request.
        var middleware = new CsrfMiddleware(new CsrfOptions());
        var req = new HttpRequest { Method = HttpMethod.POST, Path = "/echo", Headers = new Dictionary<string, string>() };
        var ctx = new HttpContext(req, new HttpResponse(), new ServiceCollection().BuildServiceProvider());

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(403, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Csrf_CookieName_OrdinalComparison_PreventsSubstringFalsePositive()
    {
        // Regression: GetCsrfCookie previously used the default (CurrentCulture) comparison,
        // which could differ from HasCsrfCookie's Ordinal check on non-English systems.
        // Both paths must now use Ordinal — a cookie named "MY-XSRF-TOKEN" must NOT match
        // when the configured name is "XSRF-TOKEN".
        var options = new CsrfOptions { CookieName = "XSRF-TOKEN" };
        var middleware = new CsrfMiddleware(options);

        var headers = new Dictionary<string, string>
        {
            // Cookie named "MY-XSRF-TOKEN" — should not match "XSRF-TOKEN"
            { "cookie", "MY-XSRF-TOKEN=some-value" },
            { "X-XSRF-TOKEN", "some-value" }
        };
        var ctx = MakeContext(HttpMethod.POST, headers);

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        // No matching cookie → validation fails → 403
        Assert.Equal(403, ctx.Response.StatusCode);
    }
}
