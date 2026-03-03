using System.Buffers.Text;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Common.Utilities;
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

    // Pre-allocated empty collections — shared across all requests that have no query/route params
    private static readonly IReadOnlyDictionary<string, string> EmptyDict =
        new Dictionary<string, string>(0);

    // Cached Content-Length header values for small sizes (0–8191 bytes)
    private static readonly AsciiString[] _contentLengthCache = BuildContentLengthCache(8192);

    private static AsciiString[] BuildContentLengthCache(int size)
    {
        var cache = new AsciiString[size];
        for (int i = 0; i < size; i++)
            cache[i] = new AsciiString(i.ToString());
        return cache;
    }

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
        // Lazy DI scope: only created if handler actually calls GetService()
        using var lazyScope = new LazyScopeProvider(_rootServices);

        // Parse request — headers are lazy (no allocations until accessed)
        var request = BuildRequest(nettyRequest);
        var response = new HttpResponse();
        var httpContext = new HttpContext(request, response, lazyScope);

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

        // Buffered response — wrap byte[] without copy
        var body = Unpooled.WrappedBuffer(response.Body);
        var nettyResponse = new DefaultFullHttpResponse(
            HttpVersion.Http11,
            HttpResponseStatus.ValueOf(response.StatusCode),
            body);

        // Content-Length: use cached AsciiString to avoid string alloc
        int bodyLen = response.Body.Length;
        nettyResponse.Headers.Set(HttpHeaderNames.ContentLength,
            response.Headers.TryGetValue("Content-Length", out var explicitCL)
                ? (ICharSequence)new AsciiString(explicitCL)
                : (bodyLen < _contentLengthCache.Length
                    ? _contentLengthCache[bodyLen]
                    : new AsciiString(bodyLen.ToString())));

        nettyResponse.Headers.Set(HttpHeaderNames.ContentType,
            response.Headers.TryGetValue("Content-Type", out var ct) ? ct : "text/plain");
        nettyResponse.Headers.Set(HttpHeaderNames.Connection, HttpHeaderValues.KeepAlive);

        foreach (var (name, value) in response.Headers)
        {
            if (!name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                nettyResponse.Headers.Set(new AsciiString(name), value);
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

        // Split path and query — no allocation when no query string
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

        // Lazy header wrapper — zero allocations unless headers are actually accessed
        var headers = new LazyNettyHeaders(nettyRequest.Headers);

        // Query dict only allocated when query string is present
        var query = queryString.Length == 0 ? EmptyDict : ParseQuery(queryString);

        // Body: read once into byte[] (only allocates when body is present)
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
        var result = new Dictionary<string, string>(4, StringComparer.OrdinalIgnoreCase);

        var span = queryString.AsSpan();
        while (!span.IsEmpty)
        {
            int amp = span.IndexOf('&');
            var pair = amp < 0 ? span : span[..amp];
            span = amp < 0 ? ReadOnlySpan<char>.Empty : span[(amp + 1)..];

            if (pair.IsEmpty) continue;
            int eq = pair.IndexOf('=');
            if (eq < 0)
                result[Uri.UnescapeDataString(pair.ToString())] = string.Empty;
            else
                result[Uri.UnescapeDataString(pair[..eq].ToString())] =
                    Uri.UnescapeDataString(pair[(eq + 1)..].ToString());
        }

        return result;
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception exception)
    {
        ctx.CloseAsync();
    }

    // ── LazyScopeProvider ──────────────────────────────────────────────────
    // Only allocates an IServiceScope if GetService() is actually called.
    // Requests with no DI dependencies pay zero scope overhead.

    private sealed class LazyScopeProvider(IServiceProvider root) : IServiceProvider, IDisposable
    {
        private IServiceScope? _scope;

        public object? GetService(Type serviceType)
        {
            _scope ??= root.CreateScope();
            return _scope.ServiceProvider.GetService(serviceType);
        }

        public void Dispose() => _scope?.Dispose();
    }

    // ── LazyNettyHeaders ──────────────────────────────────────────────────
    // Wraps DotNetty HttpHeaders without copying to a Dictionary.
    // Materializes to Dictionary<> only when iterated (e.g. by logging/CORS middleware).
    // TryGetValue does a direct linear scan over the underlying header list.

    private sealed class LazyNettyHeaders(HttpHeaders source) : IReadOnlyDictionary<string, string>
    {
        private Dictionary<string, string>? _cache;

        private Dictionary<string, string> Materialized =>
            _cache ??= Materialize();

        private Dictionary<string, string> Materialize()
        {
            var d = new Dictionary<string, string>(source.Size, StringComparer.OrdinalIgnoreCase);
            foreach (var h in source)
                d[h.Key.ToString()] = h.Value.ToString();
            return d;
        }

        public bool TryGetValue(string key, out string value)
        {
            // Fast path: if already materialized, use the dict
            if (_cache is not null)
                return _cache.TryGetValue(key, out value!);

            // Avoid full materialization: linear scan (header count is small, ~5–15)
            foreach (var h in source)
            {
                if (h.Key.ToString().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = h.Value.ToString();
                    return true;
                }
            }
            value = string.Empty;
            return false;
        }

        public string this[string key] => Materialized[key];
        public IEnumerable<string> Keys   => Materialized.Keys;
        public IEnumerable<string> Values => Materialized.Values;
        public int Count => source.Size;
        public bool ContainsKey(string key) => TryGetValue(key, out _);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Materialized.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
