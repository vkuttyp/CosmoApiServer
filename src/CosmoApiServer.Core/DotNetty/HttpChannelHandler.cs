using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Channels;
using Microsoft.Extensions.DependencyInjection;
using NetHttpMethod = System.Net.Http.HttpMethod;

namespace CosmoApiServer.Core.DotNetty;

/// <summary>
/// DotNetty channel handler that converts IFullHttpRequest → HttpContext,
/// runs the middleware pipeline, then writes the HttpResponse back.
/// </summary>
internal sealed class HttpChannelHandler : SimpleChannelInboundHandler<IFullHttpRequest>
{
    private readonly RequestDelegate _pipeline;
    private readonly IServiceProvider _rootServices;

    public HttpChannelHandler(RequestDelegate pipeline, IServiceProvider rootServices)
    {
        _pipeline = pipeline;
        _rootServices = rootServices;
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest nettyRequest)
    {
        // Fire-and-forget; exceptions handled inside
        _ = HandleAsync(ctx, nettyRequest);
    }

    private async Task HandleAsync(IChannelHandlerContext ctx, IFullHttpRequest nettyRequest)
    {
        // Create per-request DI scope
        using var scope = _rootServices.CreateScope();

        // Parse request
        var request = BuildRequest(nettyRequest);
        var response = new HttpResponse();
        var httpContext = new HttpContext(request, response, scope.ServiceProvider);

        try
        {
            await _pipeline(httpContext);
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            response.WriteText($"Internal Server Error: {ex.Message}");
        }

        // Chunked streaming response (IAsyncEnumerable<T> actions)
        if (httpContext.ChunkedBodyWriter is not null)
        {
            try
            {
                await httpContext.ChunkedBodyWriter(ctx);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ChunkedWriter ERROR] {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                ctx.CloseAsync();
            }
            return;
        }

        // Buffered response (standard actions)
        var body = Unpooled.WrappedBuffer(response.Body);
        var nettyResponse = new DefaultFullHttpResponse(
            HttpVersion.Http11,
            HttpResponseStatus.ValueOf(response.StatusCode),
            body);

        nettyResponse.Headers.Set(HttpHeaderNames.ContentLength,
            response.Headers.TryGetValue("Content-Length", out var explicitCL)
                ? explicitCL
                : response.Body.Length.ToString());
        nettyResponse.Headers.Set(HttpHeaderNames.ContentType,
            response.Headers.TryGetValue("Content-Type", out var ct) ? ct : "text/plain");
        nettyResponse.Headers.Set(HttpHeaderNames.Connection, HttpHeaderValues.KeepAlive);

        foreach (var (name, value) in response.Headers)
        {
            if (!name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                nettyResponse.Headers.Set(new global::DotNetty.Common.Utilities.AsciiString(name), value);
            }
        }

        await ctx.WriteAndFlushAsync(nettyResponse);
    }

    private static HttpRequest BuildRequest(IFullHttpRequest nettyRequest)
    {
        // Parse method
        var methodStr = nettyRequest.Method.Name;
        Http.HttpMethod method;
        try { method = HttpMethodExtensions.Parse(methodStr); }
        catch { method = Http.HttpMethod.GET; }

        // Split path and query
        var uri = nettyRequest.Uri;
        string path, queryString;
        var qIdx = uri.IndexOf('?');
        if (qIdx >= 0)
        {
            path = uri[..qIdx];
            queryString = uri[(qIdx + 1)..];
        }
        else
        {
            path = uri;
            queryString = string.Empty;
        }

        // Parse headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in nettyRequest.Headers)
            headers[h.Key.ToString()] = h.Value.ToString();

        // Parse query string
        var query = ParseQuery(queryString);

        // Read body
        byte[] body = [];
        if (nettyRequest.Content.IsReadable())
        {
            body = new byte[nettyRequest.Content.ReadableBytes];
            nettyRequest.Content.ReadBytes(body);
        }

        return new HttpRequest
        {
            Method = method,
            Path = path,
            QueryString = queryString,
            Headers = headers,
            Query = query,
            Body = body
        };
    }

    private static Dictionary<string, string> ParseQuery(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(queryString)) return result;

        foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                result[Uri.UnescapeDataString(pair)] = string.Empty;
            else
                result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        return result;
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception exception)
    {
        ctx.CloseAsync();
    }
}
