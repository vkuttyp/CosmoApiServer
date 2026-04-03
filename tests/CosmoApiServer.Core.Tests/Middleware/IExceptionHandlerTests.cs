using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class IExceptionHandlerTests
{
    private static HttpContext MakeContext(params IExceptionHandler[] handlers)
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/test" };
        var res = new HttpResponse();
        var services = new ServiceCollection();
        foreach (var h in handlers)
            services.AddSingleton<IExceptionHandler>(h);
        return new HttpContext(req, res, services.BuildServiceProvider());
    }

    [Fact]
    public async Task ExceptionHandler_FirstHandlerHandles_StopsChain()
    {
        var first = new TrackingHandler(returns: true, statusCode: 409);
        var second = new TrackingHandler(returns: true, statusCode: 503);
        var ctx = MakeContext(first, second);
        var middleware = new GlobalExceptionHandlerMiddleware();

        await middleware.InvokeAsync(ctx, _ => throw new Exception("boom"));

        Assert.True(first.WasCalled);
        Assert.False(second.WasCalled);
        Assert.Equal(409, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task ExceptionHandler_FirstDeclines_SecondHandles()
    {
        var first = new TrackingHandler(returns: false, statusCode: 0);
        var second = new TrackingHandler(returns: true, statusCode: 418);
        var ctx = MakeContext(first, second);
        var middleware = new GlobalExceptionHandlerMiddleware();

        await middleware.InvokeAsync(ctx, _ => throw new Exception("boom"));

        Assert.True(first.WasCalled);
        Assert.True(second.WasCalled);
        Assert.Equal(418, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task ExceptionHandler_NoneHandle_FallsBackToDefault()
    {
        var first = new TrackingHandler(returns: false, statusCode: 0);
        var ctx = MakeContext(first);
        var middleware = new GlobalExceptionHandlerMiddleware();

        await middleware.InvokeAsync(ctx, _ => throw new Exception("boom"));

        Assert.True(first.WasCalled);
        Assert.Equal(500, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task ExceptionHandler_NoHandlersRegistered_FallsBackToDefault()
    {
        var ctx = MakeContext(); // no handlers
        var middleware = new GlobalExceptionHandlerMiddleware();

        await middleware.InvokeAsync(ctx, _ => throw new InvalidOperationException("unhandled"));

        Assert.Equal(500, ctx.Response.StatusCode);
    }

    private sealed class TrackingHandler(bool returns, int statusCode) : IExceptionHandler
    {
        public bool WasCalled { get; private set; }

        public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
        {
            WasCalled = true;
            if (returns) context.Response.StatusCode = statusCode;
            return ValueTask.FromResult(returns);
        }
    }
}
