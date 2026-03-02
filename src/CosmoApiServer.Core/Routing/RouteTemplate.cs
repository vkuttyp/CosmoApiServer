namespace CosmoApiServer.Core.Routing;

/// <summary>
/// Parses and matches route templates like /users/{id}/orders/{orderId}
/// </summary>
public sealed class RouteTemplate
{
    private readonly string[] _segments;

    public string Template { get; }

    public RouteTemplate(string template)
    {
        Template = template;
        _segments = template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Attempts to match a request path against this template.
    /// Returns null if no match; otherwise returns extracted route values.
    /// </summary>
    public Dictionary<string, string>? TryMatch(string path)
    {
        var pathSegments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (_segments.Length != pathSegments.Length)
            return null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < _segments.Length; i++)
        {
            var seg = _segments[i];
            if (seg.StartsWith('{') && seg.EndsWith('}'))
            {
                // Parameter segment — capture value
                var paramName = seg[1..^1];
                values[paramName] = pathSegments[i];
            }
            else if (!seg.Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase))
            {
                return null; // Literal mismatch
            }
        }

        return values;
    }
}
