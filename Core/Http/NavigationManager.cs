namespace CosmoApiServer.Core.Http;

/// <summary>
/// Provides an injectable service for programmatic navigation and URL management.
/// Analogous to Blazor's NavigationManager.
///
/// Usage in controllers: inject via constructor.
/// Usage in components: available via HttpContext.RequestServices.
/// </summary>
public sealed class NavigationManager
{
    private HttpContext? _context;

    /// <summary>Gets the base URI (scheme + host + port + base path).</summary>
    public string BaseUri { get; private set; } = "/";

    /// <summary>Gets the current absolute URI.</summary>
    public string Uri => GetCurrentUri();

    /// <summary>Gets the current path (without scheme/host).</summary>
    public string Path => _context?.Request.Path ?? "/";

    /// <summary>Gets the current query string (including the leading '?').</summary>
    public string QueryString => _context?.Request.QueryString ?? string.Empty;

    /// <summary>
    /// Initializes the NavigationManager with the current request context.
    /// Called by the framework — not intended for user code.
    /// </summary>
    internal void Initialize(HttpContext context)
    {
        _context = context;
        var host = context.Request.Host ?? "localhost";
        var isHttps = context.Items.TryGetValue("__IsHttps", out var v) && v is true;
        var scheme = isHttps ? "https" : "http";
        BaseUri = $"{scheme}://{host}/";
    }

    /// <summary>
    /// Navigates to the specified URI by setting a redirect response.
    /// </summary>
    /// <param name="uri">The destination URI. Can be relative or absolute.</param>
    /// <param name="forceLoad">If true, uses a 302 redirect. Default is 302.</param>
    /// <param name="replace">If true, uses 303 (See Other) to indicate the redirect replaces the current history entry.</param>
    public void NavigateTo(string uri, bool forceLoad = false, bool replace = false)
    {
        if (_context is null)
            throw new InvalidOperationException("NavigationManager has not been initialized with an HttpContext.");

        var statusCode = replace ? 303 : 302;
        var absoluteUri = ToAbsoluteUri(uri);

        _context.Response.StatusCode = statusCode;
        _context.Response.Headers["Location"] = absoluteUri;
    }

    /// <summary>
    /// Converts a relative URI to an absolute URI.
    /// </summary>
    public string ToAbsoluteUri(string relativeUri)
    {
        if (relativeUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            relativeUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return relativeUri;

        var baseUri = BaseUri.TrimEnd('/');
        var path = relativeUri.StartsWith('/') ? relativeUri : "/" + relativeUri;
        return baseUri + path;
    }

    /// <summary>
    /// Converts an absolute URI to a relative one (path only).
    /// </summary>
    public string ToBaseRelativePath(string absoluteUri)
    {
        if (!absoluteUri.StartsWith(BaseUri, StringComparison.OrdinalIgnoreCase))
            return absoluteUri;

        return absoluteUri[BaseUri.Length..].TrimStart('/');
    }

    private string GetCurrentUri()
    {
        if (_context is null) return BaseUri;
        var qs = string.IsNullOrEmpty(_context.Request.QueryString)
            ? string.Empty
            : (_context.Request.QueryString.StartsWith('?') ? _context.Request.QueryString : "?" + _context.Request.QueryString);
        return $"{BaseUri.TrimEnd('/')}{_context.Request.Path}{qs}";
    }
}
