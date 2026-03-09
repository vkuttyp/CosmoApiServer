using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CosmoApiServer.Core.Http;
using Cosmo.Transport.Pipelines;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Handles a single HTTP/1.1 keep-alive connection using System.IO.Pipelines.
/// The hot path runs on a single thread with no context switches:
///   socket → PipeWriter → PipeReader → parser → middleware → PipeWriter → socket
/// </summary>
internal static class Http11Connection
{
    private static readonly byte[] H2cPreface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

    public static async ValueTask RunAsync(
        Stream stream,
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxBodySize,
        bool enableHttp2,
        CancellationToken ct)
    {
        // Bidirectional pipe: socket fills writer end; we read from reader end.
        var pipe = new Pipe(new PipeOptions(
            minimumSegmentSize: 4096,
            pauseWriterThreshold: maxBodySize + 4096,
            resumeWriterThreshold: maxBodySize / 2));

        var fillTask    = FillPipeAsync(stream, pipe.Writer, ct);
        var processTask = ProcessAsync(stream, pipe.Reader, pipeline, services, enableHttp2, ct);

        await Task.WhenAll(fillTask.AsTask(), processTask.AsTask());
    }

    // ── Socket → Pipe ─────────────────────────────────────────────────────

    private static async ValueTask FillPipeAsync(Stream stream, PipeWriter writer, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var memory = writer.GetMemory(4096);
                int bytesRead = await stream.ReadAsync(memory, ct);
                if (bytesRead == 0) break; // connection closed

                writer.Advance(bytesRead);
                var flush = await writer.FlushAsync(ct);
                if (flush.IsCompleted) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SocketException) { }
        finally
        {
            await writer.CompleteAsync();
        }
    }

    private static readonly byte[] Continue100 = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();

    // ── Pipe → HTTP/1.1 requests → responses ─────────────────────────────

    private static async ValueTask ProcessAsync(
        Stream stream,
        PipeReader reader,
        RequestDelegate pipeline,
        IServiceProvider services,
        bool enableHttp2,
        CancellationToken ct)
    {
        // Create a PipeWriter for the outbound stream (response side)
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));

        try
        {
            // h2c detection: peek the first bytes
            if (enableHttp2)
            {
                var result = await reader.ReadAtLeastAsync(H2cPreface.Length, ct);
                var buf = result.Buffer;

                bool isH2c = buf.Length >= H2cPreface.Length &&
                             StartsWithPreface(buf);

                reader.AdvanceTo(buf.Start, buf.End); // don't consume — let Http2 or Http11 read it

                if (isH2c)
                {
                    await Http2Connection.RunAsync(reader, writer, pipeline, services, ct);
                    return;
                }
            }

            // HTTP/1.1 keep-alive loop
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                bool parsed = Http11Parser.TryParse(ref buffer, out var req);

                // ── Expect: 100-continue ──────────────────────────────────────
                if (!parsed && Http11Parser.TryDetectExpect100(result.Buffer))
                {
                    writer.Write(Continue100);
                    await writer.FlushAsync(ct);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (!parsed)
                {
                    if (result.IsCompleted) break;
                    continue;
                }

                // Rent and build HttpContext from parsed request
                var httpContext = HttpContextPool.Rent();
                PopulateContext(httpContext, req, services, ct);
                
                httpContext.Items["__RawStream"] = stream;
                httpContext.Response.BodyWriter = writer;

                // Run the full middleware + router pipeline
                try { await pipeline(httpContext); }
                catch (Exception ex)
                {
                    httpContext.Response.StatusCode = 500;
                    httpContext.Response.WriteText($"Internal Server Error: {ex.Message}");
                }
                finally
                {
                    httpContext._disposeScope?.Dispose();
                }

                // Streaming (IAsyncEnumerable) response
                if (httpContext.StreamingBodyWriter is not null)
                {
                    await Http11Writer.WriteStreamingResponseAsync(
                        writer,
                        httpContext.Response.StatusCode,
                        httpContext.StreamingBodyWriter,
                        ct);
                    
                    HttpContextPool.Return(httpContext);
                    // streaming uses Connection: close — stop after one response
                    break;
                }

                // Standard response logic
                if (!httpContext.Response.IsStarted)
                {
                    // If not started, it was buffered. Write everything at once.
                    Http11Writer.WriteResponse(writer, httpContext.Response);
                }
                else
                {
                    // If started but headers not yet written (e.g. empty body), write them now
                    httpContext.Response.EnsureHeadersWritten();
                    httpContext.Response.End();
                }

                var flush = await writer.FlushAsync(ct);
                
                // Return to pool AFTER sending response
                HttpContextPool.Return(httpContext);

                if (flush.IsCompleted) break;

                // ── WebSocket Upgrade Handover ───────────────────────────────
                if (httpContext.Items.TryGetValue("__WebSocketUpgrade", out var upgrade) && upgrade is true)
                {
                    await writer.CompleteAsync();
                    await reader.CompleteAsync();
                    return; 
                }

                // Check if client requested close
                if (httpContext.Request.Headers.TryGetValue("Connection", out var conn) &&
                    conn.Equals("close", StringComparison.OrdinalIgnoreCase))
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SocketException) { }
        finally
        {
            await writer.CompleteAsync();
            await reader.CompleteAsync();
        }
    }

    // ── Request builder ───────────────────────────────────────────────────

    private static void PopulateContext(HttpContext ctx, ParsedRequest req, IServiceProvider services, CancellationToken ct)
    {
        // Parse path + query
        string path = req.RawTarget, queryString = string.Empty;
        int qIdx = req.RawTarget.IndexOf('?');
        if (qIdx >= 0)
        {
            path = req.RawTarget[..qIdx];
            queryString = req.RawTarget[(qIdx + 1)..];
        }

        // Method
        Http.HttpMethod method;
        try { method = HttpMethodExtensions.Parse(req.Method); }
        catch { method = Http.HttpMethod.GET; }

        // Lazy header dict (avoids copy when headers not accessed)
        var headers = new ParsedHeaderDict(req.Headers);

        // Query dict (only allocate if present)
        var query = queryString.Length == 0
            ? (IReadOnlyDictionary<string, string>)ParsedHeaderDict.Empty
            : ParseQuery(queryString);

        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.QueryString = queryString;
        ctx.Request.Headers = headers;
        ctx.Request.Query = query;
        ctx.Request.Body = req.Body;
        
        ctx.Request.ContentLength = req.ContentLength;
        ctx.Request.ContentType = req.ContentType;
        ctx.Request.Host = req.Host;
        ctx.Request.Authorization = req.Authorization;

        ctx.Initialize(services, ct);
        
        var scope = new LazyScopeProvider(services);
        ctx._disposeScope = scope;
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
                result[WebUtility.UrlDecode(pair.ToString())] = string.Empty;
            else
                result[WebUtility.UrlDecode(pair[..eq].ToString())] =
                    WebUtility.UrlDecode(pair[(eq + 1)..].ToString());
        }
        return result;
    }

    private static bool StartsWithPreface(ReadOnlySequence<byte> buffer)
    {
        int i = 0;
        foreach (var seg in buffer)
        {
            foreach (byte b in seg.Span)
            {
                if (i >= H2cPreface.Length) return true;
                if (b != H2cPreface[i++]) return false;
            }
        }
        return i >= H2cPreface.Length;
    }

    // ── Lazy-allocated header dict backed by parsed header list ───────────

    private sealed class ParsedHeaderDict(List<HeaderEntry> source)
        : IReadOnlyDictionary<string, string>
    {
        internal static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>(0);

        private Dictionary<string, string>? _cache;

        private Dictionary<string, string> Materialized =>
            _cache ??= source.ToDictionary(h => h.Name, h => h.Value, StringComparer.OrdinalIgnoreCase);

        public bool TryGetValue(string key, out string value)
        {
            if (key == null) { value = string.Empty; return false; }
            if (_cache is not null) return _cache.TryGetValue(key, out value!);
            
            foreach (var h in source)
            {
                if (h.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                { value = h.Value; return true; }
            }
            value = string.Empty;
            return false;
        }

        public string this[string key] => Materialized[key];
        public IEnumerable<string> Keys   => Materialized.Keys;
        public IEnumerable<string> Values => Materialized.Values;
        public int Count => source.Count;
        public bool ContainsKey(string key) => TryGetValue(key, out _);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Materialized.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // ── Lazy DI scope ─────────────────────────────────────────────────────

    internal sealed class LazyScopeProvider(IServiceProvider root)
        : IServiceProvider, IDisposable
    {
        private Microsoft.Extensions.DependencyInjection.IServiceScope? _scope;

        public object? GetService(Type serviceType)
        {
            _scope ??= root.CreateScope();
            return _scope.ServiceProvider.GetService(serviceType);
        }

        public void Dispose() => _scope?.Dispose();
    }
}
