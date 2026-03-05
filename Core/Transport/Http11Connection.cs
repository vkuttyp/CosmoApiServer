using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using CosmoApiServer.Core.Http;
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

    public static async Task RunAsync(
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

        await Task.WhenAll(fillTask, processTask);
    }

    // ── Socket → Pipe ─────────────────────────────────────────────────────

    private static async Task FillPipeAsync(Stream stream, PipeWriter writer, CancellationToken ct)
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

    private static async Task ProcessAsync(
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
                // When a client sends request headers with "Expect: 100-continue"
                // it waits up to ~1 s for our "100 Continue" before sending the body.
                // Detect the case (headers complete, body missing) and reply immediately.
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

                // Build HttpContext from parsed request
                var httpContext = BuildContext(req, services, ct);
                httpContext.Items["__RawStream"] = stream;

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
                    // streaming uses Connection: close — stop after one response
                    break;
                }

                // Standard buffered response
                Http11Writer.WriteResponse(writer, httpContext.Response);
                var flush = await writer.FlushAsync(ct);
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

    private static HttpContext BuildContext(ParsedRequest req, IServiceProvider services, CancellationToken ct)
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

        var request = new HttpRequest
        {
            Method      = method,
            Path        = path,
            QueryString = queryString,
            Headers     = headers,
            Query       = query,
            Body        = req.Body,
        };
        var response = new HttpResponse();
        var scope    = new LazyScopeProvider(services);
        return new HttpContext(request, response, scope, ct) { _disposeScope = scope };
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

    private sealed class ParsedHeaderDict(List<(string name, string value)> source)
        : IReadOnlyDictionary<string, string>
    {
        internal static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>(0);

        private Dictionary<string, string>? _cache;

        private Dictionary<string, string> Materialized =>
            _cache ??= source.ToDictionary(h => h.name, h => h.value, StringComparer.OrdinalIgnoreCase);

        public bool TryGetValue(string key, out string value)
        {
            if (_cache is not null) return _cache.TryGetValue(key, out value!);
            foreach (var h in source)
            {
                if (h.name.Equals(key, StringComparison.OrdinalIgnoreCase))
                { value = h.value; return true; }
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
