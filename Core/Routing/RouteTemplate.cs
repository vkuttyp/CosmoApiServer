namespace CosmoApiServer.Core.Routing;

/// <summary>
/// Parses and matches route templates like /users/{id}/orders/{orderId}.
/// Matching uses ReadOnlySpan&lt;char&gt; to avoid string allocations on the hot path.
/// </summary>
public sealed class RouteTemplate
{
    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        new Dictionary<string, string>(0);

    private readonly string[] _segments;  // pre-split template segments
    private readonly bool _hasParams;     // fast-path: no params → no dict needed

    public string Template { get; }
    public bool HasParams => _hasParams;

    public RouteTemplate(string template)
    {
        Template = template;
        _segments = template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        _hasParams = Array.Exists(_segments, s => s.StartsWith('{'));
    }

    /// <summary>
    /// Attempts to match a request path. Returns null on mismatch; otherwise extracted route values.
    /// Uses span-based walking: no heap allocations for literal-only routes.
    /// </summary>
    public IReadOnlyDictionary<string, string>? TryMatch(string path)
    {
        var span = path.AsSpan().Trim('/');

        // Count path segments without allocating
        int segCount = CountSegments(span);
        if (segCount != _segments.Length)
            return null;

        Dictionary<string, string>? values = null;

        for (int i = 0; i < _segments.Length; i++)
        {
            // Advance span to current segment
            int slash = span.IndexOf('/');
            var seg = slash < 0 ? span : span[..slash];
            span = slash < 0 ? ReadOnlySpan<char>.Empty : span[(slash + 1)..];

            var tmpl = _segments[i];
            if (tmpl[0] == '{')
            {
                // Route parameter — capture value (only borrow from pool when needed)
                values ??= RouteValuePool.Rent();
                values[tmpl[1..^1]] = seg.ToString();
            }
            else if (!seg.Equals(tmpl.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                // If we rented a dict and then mismatch, return it immediately
                if (values != null) RouteValuePool.Return(values);
                return null;
            }
        }

        // For parameterless routes or if no values were captured, return shared empty dict
        return values ?? EmptyRouteValues;
    }

    private static int CountSegments(ReadOnlySpan<char> path)
    {
        if (path.IsEmpty) return 0;
        int count = 1;
        foreach (char c in path)
            if (c == '/') count++;
        return count;
    }
}
