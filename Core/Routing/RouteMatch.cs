using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

namespace CosmoApiServer.Core.Routing;

public sealed class RouteMatch
{
    public RouteEntry Entry { get; }
    public IReadOnlyDictionary<string, string> RouteValues { get; }

    public RouteMatch(RouteEntry entry, IReadOnlyDictionary<string, string> routeValues)
    {
        Entry = entry;
        RouteValues = routeValues;
    }
}

public sealed class RouteEntry
{
    public Http.HttpMethod Method { get; }
    public RouteTemplate Template { get; }
    public RequestDelegate Handler { get; }

    public RouteEntry(Http.HttpMethod method, RouteTemplate template, RequestDelegate handler)
    {
        Method = method;
        Template = template;
        Handler = handler;
    }
}
