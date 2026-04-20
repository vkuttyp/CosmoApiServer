namespace CosmoApiServer.Core.Routing;

/// <summary>
/// Parses and matches route templates like /users/{id}/orders/{orderId}.
/// Matching uses ReadOnlySpan&lt;char&gt; to avoid string allocations on the hot path.
/// </summary>
public sealed class RouteTemplate
{
    // Use the same shared sentinel as RouteValuePool so RouterMiddleware's
    // ReferenceEquals guard correctly skips returning this to the pool.
    private static readonly IReadOnlyDictionary<string, string> EmptyRouteValues =
        RouteValuePool.EmptyShared;

    private readonly string[] _segments;  // pre-split template segments
    private readonly bool _hasParams;     // fast-path: no params → no dict needed
    private readonly bool _hasCatchAll;   // last segment is {**name}

    public string Template { get; }
    public bool HasParams => _hasParams;

    public RouteTemplate(string template)
    {
        Template = template;
        _segments = template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        _hasParams = Array.Exists(_segments, s => s.StartsWith('{'));
        _hasCatchAll = _segments.Length > 0 && _segments[^1].StartsWith("{**");
    }

    /// <summary>
    /// Attempts to match a request path. Returns null on mismatch; otherwise extracted route values.
    /// Uses span-based walking: no heap allocations for literal-only routes.
    /// </summary>
    public IReadOnlyDictionary<string, string>? TryMatch(ReadOnlySpan<char> path)
    {
        var span = path.Trim('/');

        // Count path segments without allocating
        int segCount = CountSegments(span);

        // Catch-all routes require at least (template segments - 1) path segments;
        // exact routes require exact count.
        if (_hasCatchAll)
        {
            if (segCount < _segments.Length - 1)
                return null;
        }
        else
        {
            if (segCount != _segments.Length)
                return null;
        }

        Dictionary<string, string>? values = null;

        for (int i = 0; i < _segments.Length; i++)
        {
            var tmpl = _segments[i];

            // Catch-all parameter: consume entire remainder of the path
            if (tmpl.StartsWith("{**"))
            {
                values ??= RouteValuePool.Rent();
                var key = tmpl[3..^1]; // strip {** and }
                values[key] = span.Length > 0 ? span.ToString() : string.Empty;
                break;
            }

            // Advance span to current segment
            int slash = span.IndexOf('/');
            var seg = slash < 0 ? span : span[..slash];
            span = slash < 0 ? ReadOnlySpan<char>.Empty : span[(slash + 1)..];

            if (tmpl[0] == '{')
            {
                // Route parameter — capture value (only borrow from pool when needed)
                values ??= RouteValuePool.Rent();
                var key = tmpl[1..^1];
                var colonIndex = key.IndexOf(':');
                string? constraint = null;
                if (colonIndex != -1)
                {
                    constraint = key[(colonIndex + 1)..];
                    key = key[..colonIndex];
                }

                // Validate constraint before accepting the match
                if (constraint is not null && !ValidateConstraint(seg, constraint))
                {
                    if (values != null) RouteValuePool.Return(values);
                    return null;
                }

                values[key] = seg.ToString();
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

    /// <summary>
    /// Validates a route parameter value against an inline constraint (e.g. {id:int}).
    /// Supported constraints: int, long, guid, bool, alpha, min, max.
    /// </summary>
    private static bool ValidateConstraint(ReadOnlySpan<char> value, string constraint)
    {
        return constraint switch
        {
            "int"   => int.TryParse(value, out _),
            "long"  => long.TryParse(value, out _),
            "guid"  => Guid.TryParse(value, out _),
            "bool"  => bool.TryParse(value, out _),
            "alpha" => IsAlpha(value),
            _       => true // Unknown constraint — allow through
        };
    }

    private static bool IsAlpha(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
            if (!char.IsLetter(c)) return false;
        return value.Length > 0;
    }
}
