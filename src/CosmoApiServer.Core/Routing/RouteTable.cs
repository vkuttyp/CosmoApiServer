using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

namespace CosmoApiServer.Core.Routing;

public sealed class RouteTable
{
    private readonly List<RouteEntry> _routes = [];

    public void Add(Http.HttpMethod method, string template, RequestDelegate handler)
    {
        _routes.Add(new RouteEntry(method, new RouteTemplate(template), handler));
    }

    public RouteMatch? Match(Http.HttpMethod method, string path)
    {
        // Strip query string from path
        var cleanPath = path.Contains('?') ? path[..path.IndexOf('?')] : path;

        foreach (var entry in _routes)
        {
            if (entry.Method != method) continue;
            var values = entry.Template.TryMatch(cleanPath);
            if (values is not null)
                return new RouteMatch(entry, values);
        }

        return null;
    }
}
