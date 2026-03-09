using System.IO.Compression;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class CompressionTests
{
    private static HttpContext MakeContext(string acceptEncoding = "gzip")
    {
        var headers = new Dictionary<string, string> { { "accept-encoding", acceptEncoding } };
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/test", Headers = headers };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task ResponseCompression_CompressesLargeJsonResponse()
    {
        var options = new ResponseCompressionOptions { MinimumSize = 10 };
        var middleware = new ResponseCompressionMiddleware(options);
        var ctx = MakeContext();
        
        // Setup a response that should be compressed
        ctx.Response.Headers["Content-Type"] = "application/json";
        var longMessage = string.Concat(Enumerable.Repeat("This is a long message that should definitely be compressed by the middleware. ", 10));
        ctx.Response.WriteJson(new { message = longMessage });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal("gzip", ctx.Response.Headers["Content-Encoding"]);
        Assert.Contains("Accept-Encoding", ctx.Response.Headers["Vary"]);
        
        // Verify we can decompress it
        using var ms = new MemoryStream(ctx.Response.Body);
        using var gzip = new GZipStream(ms, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var decompressed = await reader.ReadToEndAsync();
        
        Assert.Contains("This is a long message", decompressed);
    }

    [Fact]
    public async Task ResponseCompression_SkipsSmallResponse()
    {
        var options = new ResponseCompressionOptions { MinimumSize = 1000 };
        var middleware = new ResponseCompressionMiddleware(options);
        var ctx = MakeContext();
        
        ctx.Response.Headers["Content-Type"] = "application/json";
        ctx.Response.WriteJson(new { msg = "small" });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.False(ctx.Response.Headers.ContainsKey("Content-Encoding"));
    }

    [Fact]
    public async Task ResponseCompression_SkipsWhenNoAcceptEncoding()
    {
        var options = new ResponseCompressionOptions { MinimumSize = 10 };
        var middleware = new ResponseCompressionMiddleware(options);
        var ctx = MakeContext("identity");
        
        ctx.Response.Headers["Content-Type"] = "application/json";
        ctx.Response.WriteJson(new { msg = "this is long enough but encoding not accepted" });

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.False(ctx.Response.Headers.ContainsKey("Content-Encoding"));
    }
}
