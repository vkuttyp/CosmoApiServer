using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

namespace CosmoApiServer.Core.Routing;

/// <summary>
/// Filter that wraps a convention-route endpoint handler.
/// Analogous to ASP.NET Core's IEndpointFilter.
/// </summary>
public interface IEndpointFilter
{
    ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next);
}

public sealed class EndpointFilterInvocationContext(HttpContext httpContext, object?[] arguments)
{
    public HttpContext HttpContext => httpContext;
    public object?[] Arguments => arguments;

    public T GetArgument<T>(int index) => (T)arguments[index]!;
}

public delegate ValueTask<object?> EndpointFilterDelegate(EndpointFilterInvocationContext context);

/// <summary>
/// Wraps a <see cref="RequestDelegate"/> with a chain of <see cref="IEndpointFilter"/>s.
/// </summary>
internal static class EndpointFilterPipeline
{
    public static RequestDelegate Build(RequestDelegate handler, IReadOnlyList<IEndpointFilter> filters)
    {
        if (filters.Count == 0) return handler;

        return async ctx =>
        {
            // Build the filter pipeline (innermost = actual handler)
            EndpointFilterDelegate pipeline = async invocationCtx =>
            {
                await handler(invocationCtx.HttpContext);
                return null;
            };

            // Wrap outer-to-inner
            for (int i = filters.Count - 1; i >= 0; i--)
            {
                var filter = filters[i];
                var next = pipeline;
                pipeline = invocationCtx => filter.InvokeAsync(invocationCtx, next);
            }

            var invCtx = new EndpointFilterInvocationContext(ctx, []);
            await pipeline(invCtx);
        };
    }
}
