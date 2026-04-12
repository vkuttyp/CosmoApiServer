using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

/// <summary>
/// Serves Blazor WebAssembly pre-compressed framework files.
///
/// The Blazor publish pipeline emits Brotli (.br) and GZip (.gz) variants of every
/// _framework/ file alongside the uncompressed originals. Serving these pre-compressed
/// files is significantly faster than compressing them on the fly — dotnet.native.wasm
/// is typically 30–60 MB before compression.
///
/// This middleware intercepts GET/HEAD requests whose path starts with /_framework/,
/// checks whether the client accepts Brotli or GZip, and if a pre-compressed variant
/// exists on disk it streams that file directly with the appropriate Content-Encoding
/// and Content-Type headers. All other requests fall through to the next middleware
/// (StaticFileMiddleware for uncompressed files, then SpaFallback).
///
/// Register before <see cref="StaticFileMiddleware"/>:
/// <code>
/// builder.UseBlazorWasm("path/to/wwwroot");
/// </code>
/// </summary>
public sealed class BlazorWasmMiddleware : IMiddleware
{
    private readonly string _root;

    // MIME types for the original (pre-compression-extension-stripped) Blazor framework files.
    // These are set on the response even when serving the .br/.gz variant so browsers know
    // the decoded content type.
    private static readonly Dictionary<string, string> _mime = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".wasm",   "application/wasm" },
        { ".js",     "application/javascript" },
        { ".mjs",    "application/javascript" },
        { ".json",   "application/json" },
        { ".dll",    "application/octet-stream" },
        { ".pdb",    "application/octet-stream" },
        { ".dat",    "application/octet-stream" },
        { ".blat",   "application/octet-stream" },
        { ".webcil", "application/octet-stream" },
    };

    public BlazorWasmMiddleware(string rootPath)
    {
        _root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Method != Http.HttpMethod.GET &&
            context.Request.Method != Http.HttpMethod.HEAD)
        {
            await next(context);
            return;
        }

        var requestPath = context.Request.Path;

        // Only intercept _framework/ — other static files are handled by StaticFileMiddleware
        if (!requestPath.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, requestPath.TrimStart('/')));

        // Path traversal guard
        if (!fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var acceptEncoding = context.Request.Headers.TryGetValue("Accept-Encoding", out var ae) ? ae : "";

        // Prefer Brotli over GZip — smaller files, same browser support for WASM use cases
        var (compressedPath, encoding) = ResolveCompressed(fullPath, acceptEncoding);

        if (compressedPath is null)
        {
            // No pre-compressed variant or browser doesn't support it — fall through to StaticFileMiddleware
            await next(context);
            return;
        }

        var originalExt = Path.GetExtension(fullPath);
        var contentType = _mime.GetValueOrDefault(originalExt, "application/octet-stream");
        var fileInfo    = new FileInfo(compressedPath);
        var etag        = $"\"{fileInfo.LastWriteTimeUtc.Ticks:x}-{fileInfo.Length:x}\"";

        if (context.Request.Headers.TryGetValue("If-None-Match", out var inm) && inm == etag)
        {
            context.Response.StatusCode = 304;
            return;
        }

        context.Response.Headers["Content-Type"]     = contentType;
        context.Response.Headers["Content-Encoding"] = encoding!;
        // Blazor framework files are content-addressed — safe to cache indefinitely
        context.Response.Headers["Cache-Control"]    = "public, max-age=31536000, immutable";
        context.Response.Headers["ETag"]             = etag;
        // Tell proxies/CDNs to store separate copies per encoding
        context.Response.Headers["Vary"]             = "Accept-Encoding";
        context.Response.StatusCode = 200;

        if (context.Request.Method == Http.HttpMethod.HEAD)
        {
            context.Response.Headers["Content-Length"] = fileInfo.Length.ToString();
            return;
        }

        await context.Response.SendFileAsync(compressedPath, context.RequestAborted);
    }

    private static (string? path, string? encoding) ResolveCompressed(string fullPath, string acceptEncoding)
    {
        if (acceptEncoding.Contains("br", StringComparison.OrdinalIgnoreCase))
        {
            var br = fullPath + ".br";
            if (File.Exists(br)) return (br, "br");
        }

        if (acceptEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            var gz = fullPath + ".gz";
            if (File.Exists(gz)) return (gz, "gzip");
        }

        return (null, null);
    }
}
