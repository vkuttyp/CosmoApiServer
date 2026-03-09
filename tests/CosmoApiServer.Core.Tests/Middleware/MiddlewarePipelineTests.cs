using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class MiddlewarePipelineTests
{
    private static HttpContext MakeContext()
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/test" };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task Pipeline_SingleMiddleware_Executes()
    {
        var pipeline = new MiddlewarePipeline();
        var executed = false;

        pipeline.UseInstance(new LambdaMiddleware(async (ctx, next) =>
        {
            executed = true;
            await next(ctx);
        }));

        var built = pipeline.Build(_ => ValueTask.CompletedTask);
        await built(MakeContext());

        Assert.True(executed);
    }

    [Fact]
    public async Task Pipeline_OrderPreserved()
    {
        var pipeline = new MiddlewarePipeline();
        var order = new List<int>();

        pipeline.UseInstance(new LambdaMiddleware(async (ctx, next) => { order.Add(1); await next(ctx); }));
        pipeline.UseInstance(new LambdaMiddleware(async (ctx, next) => { order.Add(2); await next(ctx); }));
        pipeline.UseInstance(new LambdaMiddleware(async (ctx, next) => { order.Add(3); await next(ctx); }));

        var built = pipeline.Build(_ => ValueTask.CompletedTask);
        await built(MakeContext());

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public async Task Pipeline_MiddlewareCanShortCircuit()
    {
        var pipeline = new MiddlewarePipeline();
        var terminalCalled = false;

        pipeline.UseInstance(new LambdaMiddleware((ctx, _) =>
        {
            ctx.Response.StatusCode = 401;
            return ValueTask.CompletedTask; // short-circuit: do not call next
        }));

        var built = pipeline.Build(ctx => { terminalCalled = true; return ValueTask.CompletedTask; });
        var ctx = MakeContext();
        await built(ctx);

        Assert.False(terminalCalled);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    private sealed class LambdaMiddleware(Func<HttpContext, RequestDelegate, ValueTask> fn) : IMiddleware
    {
        public ValueTask InvokeAsync(HttpContext context, RequestDelegate next) => fn(context, next);
    }
}
