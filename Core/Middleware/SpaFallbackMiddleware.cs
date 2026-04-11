using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class SpaFallbackOptions
{
    public string RootPath { get; set; } = "wwwroot";
    public string DefaultFile { get; set; } = "index.html";
    public string[] ExcludedPrefixes { get; set; } = ["/api", "/openapi.json", "/swagger", "/health"];
}

/// <summary>
/// Serves a SPA entry document for client-side routes after static file lookup misses.
/// </summary>
public sealed class SpaFallbackMiddleware : IMiddleware
{
    private readonly string _root;
    private readonly string _defaultFile;
    private readonly string[] _excludedPrefixes;

    public SpaFallbackMiddleware(SpaFallbackOptions options)
    {
        _root = Path.GetFullPath(options.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        _defaultFile = options.DefaultFile;
        _excludedPrefixes = options.ExcludedPrefixes
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(static p => p.StartsWith('/') ? p : "/" + p)
            .ToArray();
    }

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!ShouldServeFallback(context.Request))
        {
            await next(context);
            return;
        }

        var defaultFilePath = Path.GetFullPath(Path.Combine(_root, _defaultFile));
        if (!defaultFilePath.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(defaultFilePath))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = 200;
        context.Response.Headers["Content-Type"] = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(defaultFilePath, context.RequestAborted);
    }

    private bool ShouldServeFallback(HttpRequest request)
    {
        if (request.Method != CosmoApiServer.Core.Http.HttpMethod.GET &&
            request.Method != CosmoApiServer.Core.Http.HttpMethod.HEAD)
        {
            return false;
        }

        var path = string.IsNullOrWhiteSpace(request.Path) ? "/" : request.Path;

        if (IsExcluded(path) || HasFileExtension(path))
            return false;

        if (request.Headers.TryGetValue("Accept", out var accept) &&
            !string.IsNullOrWhiteSpace(accept) &&
            !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
            !accept.Contains("*/*", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private bool IsExcluded(string path)
    {
        foreach (var prefix in _excludedPrefixes)
        {
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasFileExtension(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        var lastDot = path.LastIndexOf('.');
        return lastDot > lastSlash;
    }
}
