using System.IO.Compression;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class RequestDecompressionOptions
{
    /// <summary>Maximum decompressed body size in bytes. Default 50 MB.</summary>
    public long MaxDecompressedBodySize { get; set; } = 50L * 1024 * 1024;
}

/// <summary>
/// Decompresses request bodies encoded with gzip, deflate, or br (Brotli).
/// Reads Content-Encoding header and replaces request.Body with the decompressed bytes.
/// </summary>
public sealed class RequestDecompressionMiddleware(RequestDecompressionOptions options) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var req = context.Request;

        if (!req.Headers.TryGetValue("Content-Encoding", out var encoding) || string.IsNullOrEmpty(encoding))
        {
            await next(context);
            return;
        }

        var enc = encoding.Trim().ToLowerInvariant();

        // Already have buffered body bytes
        if (req.Body.Length > 0)
        {
            req.Body = await DecompressAsync(req.Body, enc);
            req.ContentLength = req.Body.Length;
        }
        else if (req.BodyStream != Stream.Null)
        {
            using var ms = new MemoryStream();
            await using var decomp = WrapStream(req.BodyStream, enc);
            await decomp.CopyToAsync(ms);
            req.Body = ms.ToArray();
            req.BodyStream = new MemoryStream(req.Body);
            req.ContentLength = req.Body.Length;
        }

        await next(context);
    }

    private async Task<byte[]> DecompressAsync(byte[] compressed, string encoding)
    {
        using var input = new MemoryStream(compressed);
        using var output = new MemoryStream();
        await using var decomp = WrapStream(input, encoding);
        await decomp.CopyToAsync(output);
        return output.ToArray();
    }

    private static Stream WrapStream(Stream inner, string encoding) => encoding switch
    {
        "gzip"    => new GZipStream(inner, CompressionMode.Decompress, leaveOpen: true),
        "deflate" => new DeflateStream(inner, CompressionMode.Decompress, leaveOpen: true),
        "br"      => new BrotliStream(inner, CompressionMode.Decompress, leaveOpen: true),
        _         => inner   // unknown encoding — pass through unchanged
    };
}
