using System.IO;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

/// <summary>
/// Minimal middleware to serve static files from a root directory.
/// </summary>
public class StaticFileMiddleware(string rootPath) : IMiddleware
{
    // Ensure root path ends with directory separator to prevent prefix-match bypasses
    // (e.g., /var/wwwevil passing StartsWith("/var/www"))
    private readonly string _root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    private static readonly Dictionary<string, string> _mimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".html", "text/html" },
        { ".htm",  "text/html" },
        { ".css",  "text/css" },
        { ".js",   "application/javascript" },
        { ".json", "application/json" },
        { ".png",  "image/png" },
        { ".jpg",  "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".gif",  "image/gif" },
        { ".svg",  "image/svg+xml" },
        { ".ico",  "image/x-icon" },
        { ".txt",  "text/plain" },
        { ".pdf",  "application/pdf" }
    };

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Only handle GET requests
        if (context.Request.Method != CosmoApiServer.Core.Http.HttpMethod.GET)
        {
            await next(context);
            return;
        }

        string path = context.Request.Path;
        if (path == "/") path = "/index.html";

        string fullPath = Path.GetFullPath(Path.Combine(_root, path.TrimStart('/')));

        // Security check: ensure the file is within the root directory
        if (!fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (File.Exists(fullPath))
        {
            var ext = Path.GetExtension(fullPath);
            var contentType = _mimeTypes.GetValueOrDefault(ext, "application/octet-stream");

            context.Response.StatusCode = 200;
            context.Response.Headers["Content-Type"] = contentType;
            await context.Response.SendFileAsync(fullPath, context.RequestAborted);
            return;
        }

        await next(context);
    }
}
