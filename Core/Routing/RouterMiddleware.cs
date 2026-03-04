using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

namespace CosmoApiServer.Core.Routing;

/// <summary>
/// Terminal middleware that dispatches matched routes to their handlers.
/// Returns 404 if no route matches.
/// </summary>
public sealed class RouterMiddleware : IMiddleware
{
    private readonly RouteTable _routeTable;

    public RouterMiddleware(RouteTable routeTable) => _routeTable = routeTable;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var match = _routeTable.Match(context.Request.Method, context.Request.Path);

        if (match is null)
        {
            context.Response.StatusCode = 404;
            context.Response.WriteText("Not Found");
            return;
        }

        // Inject route values into request
        context.Request.RouteValues = match.RouteValues;

        await match.Entry.Handler(context);
    }
}
