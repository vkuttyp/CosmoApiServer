using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using System.Collections.Concurrent;

namespace CosmoApiServer.Core.Routing;

public sealed class RouteTable
{
    // Group routes by HTTP method
    private readonly Dictionary<Http.HttpMethod, List<RouteEntry>> _routes = new();
    
    // Fast cache for previously matched paths
    private readonly ConcurrentDictionary<(Http.HttpMethod, string), RouteMatch> _cache = new();

    public void Add(Http.HttpMethod method, string template, RequestDelegate handler)
    {
        if (!_routes.TryGetValue(method, out var list))
        {
            list = new List<RouteEntry>();
            _routes[method] = list;
        }

        var routeTemplate = new RouteTemplate(template);
        list.Add(new RouteEntry(method, routeTemplate, handler));
        
        // Clear cache when routes change
        _cache.Clear();
    }

    public RouteMatch? Match(Http.HttpMethod method, string path)
    {
        // 1. Try Cache (only static/non-parameterized routes are cached)
        if (_cache.TryGetValue((method, path), out var cachedMatch))
        {
            return cachedMatch;
        }

        if (!_routes.TryGetValue(method, out var routes))
            return null;

        var cleanPath = path.AsSpan();
        int qIdx = cleanPath.IndexOf('?');
        if (qIdx >= 0) cleanPath = cleanPath[..qIdx];
        cleanPath = cleanPath.Trim('/');

        foreach (var entry in routes)
        {
            var values = entry.Template.TryMatch(cleanPath);
            if (values is not null)
            {
                var match = new RouteMatch(entry, values);

                // Only cache non-parameterized routes to prevent unbounded memory growth.
                // Parameterized routes (e.g., /users/{id}) produce unique paths that would
                // leak memory indefinitely if cached.
                if (!entry.Template.HasParams)
                {
                    _cache.TryAdd((method, path), match);
                }

                return match;
            }
        }

        return null;
    }
}
