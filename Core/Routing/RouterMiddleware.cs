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

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
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

        try
        {
            await match.Entry.Handler(context);
        }
        finally
        {
            // Return dictionary to pool if it was a real dictionary (and not the shared empty one)
            if (match.RouteValues is Dictionary<string, string> dict && dict.Count > 0)
            {
                RouteValuePool.Return(dict);
            }
        }
    }
}
