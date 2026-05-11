using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Pipeline-based HTTP/1.1 reverse proxy forwarder.
/// Opens raw TCP sockets to upstreams, writes requests via <see cref="Http11RequestWriter"/>,
/// parses responses via <see cref="Http11ResponseParser"/>, and splices the response body
/// directly from upstream PipeReader to the downstream PipeWriter — zero intermediate copies.
///
/// Connections are pooled per (host, port) for keep-alive reuse.
/// </summary>
public sealed class PipelineHttpForwarder : IAsyncDisposable
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE",
        "Trailer", "Upgrade", "Proxy-Authorization", "Proxy-Authenticate"
    };

    private readonly ConcurrentDictionary<string, ConcurrentBag<PooledConnection>> _pool = new();
    private readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(15);
    private readonly TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
    private bool _disposed;

    /// <summary>
    /// Forward an inbound request to the specified upstream and write the response
    /// directly to the downstream context. The response body is spliced via PipeReader/PipeWriter
    /// with no intermediate buffer copies.
    /// </summary>
    public async Task ForwardAsync(
        HttpContext context,
        string upstreamScheme,
        string upstreamHost,
        int upstreamPort,
        string pathAndQuery,
        IReadOnlyDictionary<string, string>? extraRequestHeaders,
        IReadOnlyDictionary<string, string>? extraResponseHeaders,
        CancellationToken ct)
    {
        var poolKey = $"{upstreamHost}:{upstreamPort}";
        var conn = RentConnection(poolKey) ?? await ConnectAsync(upstreamHost, upstreamPort, ct);

        try
        {
            // Build forwarding headers — skip hop-by-hop and Host
            var forwardHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in context.Request.Headers)
            {
                if (HopByHopHeaders.Contains(name)) continue;
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                forwardHeaders[name] = value;
            }

            // Add proxy headers
            forwardHeaders["X-Forwarded-Host"] = context.Request.Host ?? "";
            forwardHeaders["X-Forwarded-Proto"] = upstreamScheme;
            if (context.Items.TryGetValue("__RemoteIP", out var remoteIp) && remoteIp is string ip)
                forwardHeaders["X-Forwarded-For"] = ip;

            // Merge extra request headers
            if (extraRequestHeaders is not null)
            {
                foreach (var (name, value) in extraRequestHeaders)
                    forwardHeaders[name] = value;
            }

            // Connection: keep-alive for pooling
            forwardHeaders["Connection"] = "keep-alive";

            long requestContentLength = context.Request.ContentLength;
            bool requestChunked = context.Request.IsChunked;
            byte[]? bufferedBody = null;

            // For chunked inbound requests we buffer the (chunked-decoded)
            // body in memory and forward as a fixed-length request instead of
            // re-emitting HTTP chunks. The chunked-to-chunked passthrough is
            // fragile because we'd have to faithfully replicate inbound
            // boundaries — and AWS S3 SigV4 streaming uploads layer their own
            // aws-chunked framing inside the HTTP chunks, expecting the
            // outermost framing to behave a specific way. Re-chunking can
            // cause the origin (CosmoS3) to wait indefinitely for body bytes
            // it thinks are missing. Buffering removes that class of bug.
            // Cap at 100 MiB so a hostile or runaway upload can't OOM the
            // proxy; beyond that we leave the body un-forwarded (the origin
            // will see headers only and respond accordingly).
            const int BufferedBodyCap = 100 * 1024 * 1024;
            if (requestChunked && context.Request.BodyStream != Stream.Null)
            {
                using var ms = new MemoryStream();
                var buf = ArrayPool<byte>.Shared.Rent(8192);
                try
                {
                    int read;
                    while ((read = await context.Request.BodyStream.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
                    {
                        if (ms.Length + read > BufferedBodyCap)
                        {
                            // Body exceeds cap; drain remaining bytes but discard them
                            // so the inbound pipe is left in a clean state for keep-alive.
                            // The origin will see an incomplete body but that's better than
                            // OOMing the proxy.
                            while (await context.Request.BodyStream.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false) > 0) { }
                            break;
                        }
                        ms.Write(buf, 0, read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }

                bufferedBody = ms.ToArray();
                requestContentLength = bufferedBody.Length;
                requestChunked = false;
            }

            // Pick the Host header sent to the upstream. Default is the
            // upstream's own host:port (preserves existing behaviour and keeps
            // upstream backends — including HttpListener-style hosts that
            // bind to a specific prefix — working). Operators who need the
            // original inbound Host (e.g. S3-compatible upstreams that verify
            // SigV4 signatures over the Host header) can opt in via
            // extraRequestHeaders["Host"], populated by the Caddyfile
            // `header_up Host=<value>` directive. The token "$host" expands
            // to the original inbound Host header.
            string forwardedHost = $"{upstreamHost}:{upstreamPort}";
            if (extraRequestHeaders is not null
                && extraRequestHeaders.TryGetValue("Host", out var hostOverride)
                && !string.IsNullOrWhiteSpace(hostOverride))
            {
                forwardedHost = hostOverride.Equals("$host", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(context.Request.Host)
                    ? context.Request.Host!
                    : hostOverride;
            }

            // Write request to upstream pipe
            Http11RequestWriter.WriteRequest(
                conn.Writer,
                context.Request.Method.ToString(),
                pathAndQuery,
                forwardedHost,
                forwardHeaders,
                requestContentLength,
                requestChunked);

            // Write request body if present
            if (bufferedBody is { Length: > 0 })
            {
                // Chunked inbound was buffered above; emit as raw bytes with Content-Length.
                conn.Writer.Write(bufferedBody);
            }
            else if (requestContentLength > 0 && context.Request.BodyReader is not null)
            {
                await Http11RequestWriter.CopyBodyAsync(conn.Writer, context.Request.BodyReader, requestContentLength, ct);
            }
            else if (requestContentLength > 0 && context.Request.Body.Length > 0)
            {
                Http11RequestWriter.WriteBody(conn.Writer, context.Request.Body);
            }

            // Flush request to upstream
            await conn.Writer.FlushAsync(ct);

            // Read response from upstream pipe
            var parsedResponse = await ReadResponseAsync(conn.Reader, ct);

            // Set downstream status
            context.Response.StatusCode = parsedResponse.StatusCode;

            // Copy response headers to downstream
            foreach (var header in parsedResponse.Headers)
            {
                if (HopByHopHeaders.Contains(header.Name)) continue;

                if (header.Name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.SetCookieHeaders.Add(header.Value);
                    continue;
                }

                context.Response.Headers[header.Name] = header.Value;
            }

            // Add extra response headers
            if (extraResponseHeaders is not null)
            {
                foreach (var (name, value) in extraResponseHeaders)
                    context.Response.Headers[name] = value;
            }

            // Splice response body: upstream PipeReader → downstream response.
            // Per RFC 7230 §3.3.3, responses to HEAD and 1xx/204/304 carry no
            // message body even when Content-Length or Transfer-Encoding say
            // otherwise. Without this guard we'd block in SpliceFixedBodyAsync
            // waiting for N bytes that the upstream will never send, hanging
            // every HEAD-via-aws-cli call (HeadObject precedes every PUT/GET
            // in modern AWS SDKs) until the proxy's 30s end-to-end timeout.
            bool responseHasNoBody =
                context.Request.Method == CosmoApiServer.Core.Http.HttpMethod.HEAD ||
                (parsedResponse.StatusCode >= 100 && parsedResponse.StatusCode < 200) ||
                parsedResponse.StatusCode == 204 ||
                parsedResponse.StatusCode == 304;

            if (responseHasNoBody)
            {
                if (parsedResponse.ContentLength >= 0)
                    context.Response.Headers["Content-Length"] = parsedResponse.ContentLength.ToString();
            }
            else if (parsedResponse.Chunked)
            {
                await SpliceChunkedBodyAsync(conn.Reader, context.Response, ct);
            }
            else if (parsedResponse.ContentLength > 0)
            {
                context.Response.Headers["Content-Length"] = parsedResponse.ContentLength.ToString();
                await SpliceFixedBodyAsync(conn.Reader, context.Response, parsedResponse.ContentLength, ct);
            }
            else if (parsedResponse.ContentLength == 0)
            {
                context.Response.Headers["Content-Length"] = "0";
            }

            context.Response.End();

            // Return connection to pool if keep-alive
            if (!parsedResponse.ConnectionClose)
            {
                conn.LastUsedUtc = DateTime.UtcNow;
                ReturnConnection(poolKey, conn);
            }
            else
            {
                await conn.DisposeAsync();
            }
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Simplified overload for common proxy patterns.
    /// </summary>
    public Task ForwardAsync(
        HttpContext context,
        string destinationBaseUrl,
        CancellationToken ct)
    {
        var uri = new Uri(destinationBaseUrl.TrimEnd('/'));
        var scheme = uri.Scheme;
        var host = uri.Host;
        var port = uri.Port;
        var qs = context.Request.QueryString;
        var pathAndQuery = context.Request.Path +
                           (string.IsNullOrEmpty(qs) ? "" : "?" + qs.TrimStart('?'));

        return ForwardAsync(context, scheme, host, port, pathAndQuery, null, null, ct);
    }

    private static async Task<ParsedResponse> ReadResponseAsync(PipeReader reader, CancellationToken ct)
    {
        while (true)
        {
            var result = await reader.ReadAsync(ct);
            var buffer = result.Buffer;

            if (Http11ResponseParser.TryParse(ref buffer, out var response))
            {
                reader.AdvanceTo(buffer.Start, buffer.Start);
                return response;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
                throw new IOException("Upstream closed connection before sending complete response headers.");
        }
    }

    /// <summary>
    /// Splice a fixed-length response body from upstream PipeReader directly into the downstream response.
    /// </summary>
    private static async Task SpliceFixedBodyAsync(PipeReader reader, HttpResponse response, long contentLength, CancellationToken ct)
    {
        long remaining = contentLength;
        while (remaining > 0)
        {
            var result = await reader.ReadAsync(ct);
            var buffer = result.Buffer;

            if (buffer.IsEmpty && result.IsCompleted)
                break;

            long toCopy = Math.Min(buffer.Length, remaining);
            foreach (var segment in buffer.Slice(0, toCopy))
            {
                response.Write(segment.Span);
            }

            reader.AdvanceTo(buffer.GetPosition(toCopy));
            remaining -= toCopy;
        }
    }

    /// <summary>
    /// Splice a chunked response body. Decodes chunked transfer encoding from upstream
    /// and writes raw bytes to the downstream response.
    /// </summary>
    private static async Task SpliceChunkedBodyAsync(PipeReader reader, HttpResponse response, CancellationToken ct)
    {
        // Use HttpBodyStream to handle chunked decoding — it already implements the full spec
        var bodyStream = new HttpBodyStream(reader, 0, chunked: true);
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int read;
            while ((read = await bodyStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                response.Write(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // ── Connection pooling ───────────────────────────────────────────────────

    private async Task<PooledConnection> ConnectAsync(string host, int port, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        socket.DualMode = true;
        socket.NoDelay = true;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            await socket.ConnectAsync(host, port, timeoutCts.Token);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        var stream = new NetworkStream(socket, ownsSocket: true);
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 4096));

        // Start filling the pipe from the socket
        var fillTask = FillPipeAsync(stream, pipe.Writer, ct);

        // Create writer for the outbound stream
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));

        return new PooledConnection(socket, stream, pipe.Reader, writer, fillTask);
    }

    private static async Task FillPipeAsync(Stream stream, PipeWriter writer, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var memory = writer.GetMemory(4096);
                int bytesRead = await stream.ReadAsync(memory, ct);
                if (bytesRead == 0) break;

                writer.Advance(bytesRead);
                var flush = await writer.FlushAsync(ct);
                if (flush.IsCompleted) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (!ct.IsCancellationRequested) { }
        finally
        {
            await writer.CompleteAsync();
        }
    }

    private PooledConnection? RentConnection(string key)
    {
        if (!_pool.TryGetValue(key, out var bag))
            return null;

        while (bag.TryTake(out var conn))
        {
            if (conn.IsExpired(_idleTimeout))
            {
                _ = conn.DisposeAsync();
                continue;
            }

            if (!conn.IsAlive)
            {
                _ = conn.DisposeAsync();
                continue;
            }

            return conn;
        }

        return null;
    }

    private void ReturnConnection(string key, PooledConnection conn)
    {
        if (_disposed)
        {
            _ = conn.DisposeAsync();
            return;
        }

        var bag = _pool.GetOrAdd(key, _ => new ConcurrentBag<PooledConnection>());
        bag.Add(conn);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        foreach (var (_, bag) in _pool)
        {
            while (bag.TryTake(out var conn))
            {
                await conn.DisposeAsync();
            }
        }
        _pool.Clear();
    }

    // ── Pooled connection ────────────────────────────────────────────────────

    private sealed class PooledConnection(
        Socket socket,
        NetworkStream stream,
        PipeReader reader,
        PipeWriter writer,
        Task fillTask) : IAsyncDisposable
    {
        public PipeReader Reader => reader;
        public PipeWriter Writer => writer;
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

        public bool IsAlive => socket.Connected && !fillTask.IsCompleted;
        public bool IsExpired(TimeSpan idle) => DateTime.UtcNow - LastUsedUtc > idle;

        public async ValueTask DisposeAsync()
        {
            try { await writer.CompleteAsync(); } catch { }
            try { await reader.CompleteAsync(); } catch { }
            try { stream.Dispose(); } catch { }
            try { socket.Dispose(); } catch { }
        }
    }
}
