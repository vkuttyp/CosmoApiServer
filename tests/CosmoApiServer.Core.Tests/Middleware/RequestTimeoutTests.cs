using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class RequestTimeoutTests
{
    private static HttpContext MakeContext()
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/test" };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task RequestTimeout_CompletesBeforeTimeout_Returns200()
    {
        var opts = new RequestTimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(5) };
        var middleware = new RequestTimeoutMiddleware(opts);
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task RequestTimeout_ExceedsTimeout_Returns504()
    {
        var opts = new RequestTimeoutOptions { DefaultTimeout = TimeSpan.FromMilliseconds(50) };
        var middleware = new RequestTimeoutMiddleware(opts);
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, async c =>
        {
            await Task.Delay(500, c.RequestAborted);
        });

        Assert.Equal(504, ctx.Response.StatusCode);
    }
}
