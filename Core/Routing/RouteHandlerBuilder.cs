using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

namespace CosmoApiServer.Core.Routing;

/// <summary>
/// Fluent builder returned from Map* methods. Allows chaining endpoint filters,
/// metadata, and other per-route configuration.
/// </summary>
public sealed class RouteHandlerBuilder(RouteTable routeTable, Http.HttpMethod method, string template, RequestDelegate handler)
{
    private readonly List<IEndpointFilter> _filters = [];

    /// <summary>Add an inline endpoint filter.</summary>
    public RouteHandlerBuilder AddEndpointFilter(IEndpointFilter filter)
    {
        _filters.Add(filter);
        Commit();
        return this;
    }

    /// <summary>Add an inline endpoint filter via delegate.</summary>
    public RouteHandlerBuilder AddEndpointFilter(
        Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> filterFunc)
    {
        _filters.Add(new DelegateEndpointFilter(filterFunc));
        Commit();
        return this;
    }

    /// <summary>Add a typed endpoint filter resolved at request time.</summary>
    public RouteHandlerBuilder AddEndpointFilter<TFilter>() where TFilter : IEndpointFilter, new()
    {
        _filters.Add(new TFilter());
        Commit();
        return this;
    }

    /// <summary>Require authentication for this route (returns 401 if User is null).</summary>
    public RouteHandlerBuilder RequireAuthorization()
    {
        _filters.Add(new DelegateEndpointFilter(async (ctx, next) =>
        {
            if (ctx.HttpContext.User is null)
            {
                ctx.HttpContext.Response.StatusCode = 401;
                ctx.HttpContext.Response.WriteJson(new { error = "Unauthorized" });
                return null;
            }
            return await next(ctx);
        }));
        Commit();
        return this;
    }

    private void Commit() =>
        routeTable.Add(method, template, EndpointFilterPipeline.Build(handler, _filters));

    private sealed class DelegateEndpointFilter(
        Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> fn) : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
            => fn(ctx, next);
    }
}
