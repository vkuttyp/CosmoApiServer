using System.IO.Compression;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class ResponseCompressionOptions
{
    public int MinimumSize { get; set; } = 1024; // 1KB
    public string[] MimeTypes { get; set; } = 
    [
        "text/plain", "text/html", "text/css", "application/javascript", 
        "application/json", "application/xml", "image/svg+xml"
    ];
}

/// <summary>
/// High-performance GZip response compression middleware.
/// </summary>
public sealed class ResponseCompressionMiddleware(ResponseCompressionOptions options) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await next(context);

        var response = context.Response;

        // Skip if already started/streaming or too small
        if (!response.IsBuffered || response.Body.Length < options.MinimumSize) return;

        // Check for Accept-Encoding: gzip
        if (!context.Request.Headers.TryGetValue("accept-encoding", out var accept) || !accept.Contains("gzip", StringComparison.OrdinalIgnoreCase))
            return;

        // Check for compatible MIME types
        if (!response.Headers.TryGetValue("content-type", out var ct) || !options.MimeTypes.Any(m => ct.StartsWith(m, StringComparison.OrdinalIgnoreCase)))
            return;

        // Compress!
        var originalBody = response.Body;
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Fastest))
        {
            await gzip.WriteAsync(originalBody);
        }

        var compressed = ms.ToArray();
        
        // Only update if compression actually reduced the size (rare for small files but possible)
        if (compressed.Length < originalBody.Length)
        {
            response.ClearBody();
            response.Write(compressed);
            response.Headers["Content-Encoding"] = "gzip";
            response.Headers["Content-Length"] = compressed.Length.ToString();
            // Vary header is essential for proxy caching
            if (response.Headers.TryGetValue("vary", out var v)) response.Headers["vary"] = v + ", Accept-Encoding";
            else response.Headers["vary"] = "Accept-Encoding";
        }
    }
}
