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
        // Only handle safe file reads
        if (context.Request.Method != CosmoApiServer.Core.Http.HttpMethod.GET &&
            context.Request.Method != CosmoApiServer.Core.Http.HttpMethod.HEAD)
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
            var fileInfo = new FileInfo(fullPath);
            var ext = Path.GetExtension(fullPath);
            var contentType = _mimeTypes.GetValueOrDefault(ext, "application/octet-stream");

            context.Response.Headers["Accept-Ranges"] = "bytes";
            context.Response.Headers["Content-Type"] = contentType;

            if (TryParseRange(context.Request.Headers, fileInfo.Length, out var start, out var end))
            {
                long count = end - start + 1;
                context.Response.StatusCode = 206;
                context.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{fileInfo.Length}";
                await context.Response.SendFileAsync(fullPath, start, count, context.RequestAborted);
                return;
            }

            if (HasUnsatisfiableRange(context.Request.Headers, fileInfo.Length))
            {
                context.Response.StatusCode = 416;
                context.Response.Headers["Content-Range"] = $"bytes */{fileInfo.Length}";
                return;
            }

            context.Response.StatusCode = 200;
            await context.Response.SendFileAsync(fullPath, context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool TryParseRange(IReadOnlyDictionary<string, string> headers, long fileLength, out long start, out long end)
    {
        start = end = 0;
        if (!headers.TryGetValue("Range", out var rangeHeader) && !headers.TryGetValue("range", out rangeHeader))
            return false;

        if (!TryParseSingleRange(rangeHeader, fileLength, out start, out end))
            return false;

        return true;
    }

    private static bool HasUnsatisfiableRange(IReadOnlyDictionary<string, string> headers, long fileLength)
    {
        if (!headers.TryGetValue("Range", out var rangeHeader) && !headers.TryGetValue("range", out rangeHeader))
            return false;

        return !TryParseSingleRange(rangeHeader, fileLength, out _, out _) &&
               rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) &&
               !rangeHeader.Contains(',', StringComparison.Ordinal);
    }

    private static bool TryParseSingleRange(string rangeHeader, long fileLength, out long start, out long end)
    {
        start = end = 0;
        if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return false;

        var spec = rangeHeader["bytes=".Length..].Trim();
        if (spec.Length == 0 || spec.Contains(',', StringComparison.Ordinal))
            return false;

        int dash = spec.IndexOf('-');
        if (dash < 0)
            return false;

        var startPart = spec[..dash].Trim();
        var endPart = spec[(dash + 1)..].Trim();

        if (startPart.Length == 0)
        {
            if (!long.TryParse(endPart, out var suffixLength) || suffixLength <= 0)
                return false;

            if (suffixLength >= fileLength)
            {
                start = 0;
                end = fileLength - 1;
                return fileLength > 0;
            }

            start = fileLength - suffixLength;
            end = fileLength - 1;
            return true;
        }

        if (!long.TryParse(startPart, out start) || start < 0 || start >= fileLength)
            return false;

        if (endPart.Length == 0)
        {
            end = fileLength - 1;
            return true;
        }

        if (!long.TryParse(endPart, out end) || end < start)
            return false;

        if (end >= fileLength)
            end = fileLength - 1;

        return true;
    }
}
