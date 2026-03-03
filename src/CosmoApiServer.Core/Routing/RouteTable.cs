using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

namespace CosmoApiServer.Core.Routing;

public sealed class RouteTable
{
    // Group routes by HTTP method for O(1) method dispatch
    private readonly Dictionary<Http.HttpMethod, List<RouteEntry>> _routesByMethod =
        new Dictionary<Http.HttpMethod, List<RouteEntry>>();

    public void Add(Http.HttpMethod method, string template, RequestDelegate handler)
    {
        if (!_routesByMethod.TryGetValue(method, out var list))
        {
            list = new List<RouteEntry>();
            _routesByMethod[method] = list;
        }
        list.Add(new RouteEntry(method, new RouteTemplate(template), handler));
    }

    public RouteMatch? Match(Http.HttpMethod method, string path)
    {
        // Strip query string from path
        var cleanPath = path.Contains('?') ? path[..path.IndexOf('?')] : path;

        if (!_routesByMethod.TryGetValue(method, out var routes))
            return null;

        foreach (var entry in routes)
        {
            var values = entry.Template.TryMatch(cleanPath);
            if (values is not null)
                return new RouteMatch(entry, values);
        }

        return null;
    }
}
