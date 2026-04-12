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
        { ".html",  "text/html; charset=utf-8" },
        { ".htm",   "text/html; charset=utf-8" },
        { ".css",   "text/css" },
        { ".js",    "application/javascript" },
        // .mjs is critical for Vite — browsers reject ES module chunks served as octet-stream
        { ".mjs",   "application/javascript" },
        { ".json",  "application/json" },
        { ".map",   "application/json" },           // source maps
        { ".png",   "image/png" },
        { ".jpg",   "image/jpeg" },
        { ".jpeg",  "image/jpeg" },
        { ".gif",   "image/gif" },
        { ".svg",   "image/svg+xml" },
        { ".ico",   "image/x-icon" },
        { ".webp",  "image/webp" },
        { ".txt",   "text/plain" },
        { ".pdf",   "application/pdf" },
        { ".wasm",  "application/wasm" },
        { ".woff",  "font/woff" },
        { ".woff2", "font/woff2" },
        { ".ttf",   "font/ttf" },
        { ".otf",   "font/otf" },
    };

    // Matches Vite fingerprinted filenames: "name-Cx3kD9aB.js", "chunk-AbCd1234.mjs" etc.
    private static readonly System.Text.RegularExpressions.Regex _fingerprinted =
        new(@"-[A-Za-z0-9]{8,}\.[^.]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

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

            // ETag based on last-write timestamp + file size — cheap and stable.
            var etag = $"\"{fileInfo.LastWriteTimeUtc.Ticks:x}-{fileInfo.Length:x}\"";

            context.Response.Headers["Accept-Ranges"] = "bytes";
            context.Response.Headers["Content-Type"]  = contentType;
            context.Response.Headers["ETag"]           = etag;
            context.Response.Headers["Cache-Control"]  = GetCacheControl(fullPath, ext);

            // Conditional GET/HEAD — return 304 when client already has the current version.
            if (context.Request.Headers.TryGetValue("If-None-Match", out var inm) && inm == etag)
            {
                context.Response.StatusCode = 304;
                return;
            }

            // HEAD — return headers only, no body.
            if (context.Request.Method == CosmoApiServer.Core.Http.HttpMethod.HEAD)
            {
                context.Response.StatusCode = 200;
                context.Response.Headers["Content-Length"] = fileInfo.Length.ToString();
                return;
            }

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

    private static string GetCacheControl(string filePath, string ext)
    {
        // HTML must never be cached — it references fingerprinted assets by hash, so a
        // stale shell would load the wrong (possibly deleted) JS/CSS after a deploy.
        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".htm",  StringComparison.OrdinalIgnoreCase))
            return "no-cache";

        // Vite fingerprinted assets are immutable by definition — content-addressed names
        // change on every build, so a year-long cache is safe and greatly improves perf.
        if (_fingerprinted.IsMatch(Path.GetFileName(filePath)))
            return "public, max-age=31536000, immutable";

        // Non-fingerprinted static assets (fonts, images referenced directly): 1-hour cache
        // with revalidation via ETag so browsers still see updates when they do expire.
        return "public, max-age=3600";
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
