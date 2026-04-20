using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Net;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// HTTP/2 connection handler (RFC 7540).
/// Supports: SETTINGS, HEADERS, DATA, WINDOW_UPDATE, PING, RST_STREAM, GOAWAY.
/// Each HTTP/2 stream runs the app pipeline as an independent Task.
/// </summary>
internal sealed class Http2Connection
{
    // ── Frame types ───────────────────────────────────────────────────────
    private const byte FrameData         = 0x0;
    private const byte FrameHeaders      = 0x1;
    private const byte FramePriority     = 0x2;
    private const byte FrameRstStream    = 0x3;
    private const byte FrameSettings     = 0x4;
    private const byte FramePushPromise  = 0x5;
    private const byte FramePing         = 0x6;
    private const byte FrameGoaway       = 0x7;
    private const byte FrameWindowUpdate = 0x8;
    private const byte FrameContinuation = 0x9;

    // ── Flags ─────────────────────────────────────────────────────────────
    private const byte FlagEndStream   = 0x1;
    private const byte FlagEndHeaders  = 0x4;
    private const byte FlagAck         = 0x1;
    private const byte FlagPadded      = 0x8;
    private const byte FlagPriority    = 0x20;

    // ── Error codes ───────────────────────────────────────────────────────
    private const uint ErrNoError            = 0;
    private const uint ErrProtocolError      = 1;
    private const uint ErrStreamClosed       = 5;
    private const uint ErrFrameSizeError     = 6;

    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private readonly RequestDelegate _pipeline;
    private readonly IServiceProvider _services;
    private readonly CancellationToken _ct;
    private readonly string? _altSvcValue;
    private readonly ILogger? _logger;

    private readonly HpackDecoder _hpack = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Active streams: streamId → accumulated headers/data
    private readonly ConcurrentDictionary<int, Http2Stream> _streams = new();

    // Connection-level flow control (simplified — sends WINDOW_UPDATE proactively)
    private const int InitialWindowSize = 65535;

    // ── Entry point ───────────────────────────────────────────────────────

    public static async ValueTask RunAsync(
        PipeReader reader,
        PipeWriter writer,
        RequestDelegate pipeline,
        IServiceProvider services,
        CancellationToken ct,
        string? altSvcValue = null)
    {
        var conn = new Http2Connection(reader, writer, pipeline, services, ct, altSvcValue);
        await conn.RunAsync();
    }

    public static async ValueTask RunAsync(
        System.IO.Stream stream,
        RequestDelegate pipeline,
        IServiceProvider services,
        CancellationToken ct,
        string? altSvcValue = null)
    {
        var reader = PipeReader.Create(stream);
        var writer = PipeWriter.Create(stream);
        await RunAsync(reader, writer, pipeline, services, ct, altSvcValue);
    }

    private Http2Connection(PipeReader reader, PipeWriter writer,
        RequestDelegate pipeline, IServiceProvider services, CancellationToken ct, string? altSvcValue = null)
    {
        _reader = reader; _writer = writer;
        _pipeline = pipeline; _services = services; _ct = ct;
        _altSvcValue = altSvcValue;
        _logger = services.GetService<ILoggerFactory>()?.CreateLogger("CosmoApiServer.Http2");
    }

    private async ValueTask RunAsync()
    {
        try
        {
            // Consume the client connection preface (24 bytes)
            await ConsumeConnectionPreface();

            // Send server SETTINGS (empty — use defaults)
            await SendSettingsAsync();

            // Frame read loop
            while (!_ct.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync();
                if (frame is null) break;

                await DispatchFrameAsync(frame);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.IO.IOException or System.Net.Sockets.SocketException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[HTTP/2 ERROR] {ExType}", ex.GetType().Name);
            if (_logger is null) Console.Error.WriteLine($"[HTTP/2 ERROR] {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            await _reader.CompleteAsync();
            await _writer.CompleteAsync();
        }
    }

    // ── Frame read ────────────────────────────────────────────────────────

    private async ValueTask ConsumeConnectionPreface()
    {
        const int prefaceLen = 24;
        var result = await _reader.ReadAtLeastAsync(prefaceLen, _ct);
        _reader.AdvanceTo(result.Buffer.GetPosition(prefaceLen));
    }

    private async ValueTask<Http2Frame?> ReadFrameAsync()
    {
        // 9-byte frame header
        var result = await _reader.ReadAtLeastAsync(9, _ct);
        if (result.IsCompleted && result.Buffer.Length < 9) return null;

        var header = result.Buffer.Slice(0, 9);
        Span<byte> hdr = stackalloc byte[9];
        header.CopyTo(hdr);
        _reader.AdvanceTo(result.Buffer.GetPosition(9));

        int length   = (hdr[0] << 16) | (hdr[1] << 8) | hdr[2];
        byte type    = hdr[3];
        byte flags   = hdr[4];
        int streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(hdr[5..]) & 0x7FFFFFFF);

        byte[] payload = [];
        if (length > 0)
        {
            var payloadResult = await _reader.ReadAtLeastAsync(length, _ct);
            payload = new byte[length];
            payloadResult.Buffer.Slice(0, length).CopyTo(payload);
            _reader.AdvanceTo(payloadResult.Buffer.GetPosition(length));
        }

        return new Http2Frame(type, flags, streamId, payload);
    }

    // ── Frame dispatch ────────────────────────────────────────────────────

    private async ValueTask DispatchFrameAsync(Http2Frame frame)
    {
        switch (frame.Type)
        {
            case FrameSettings:
                if ((frame.Flags & FlagAck) == 0)
                    await SendSettingsAckAsync();
                break;

            case FramePing:
                if ((frame.Flags & FlagAck) == 0)
                    await SendPingAckAsync(frame.Payload);
                break;

            case FrameWindowUpdate:
                // Simplified: ignore flow control updates (for internal/LAN use)
                break;

            case FrameHeaders:
                await HandleHeadersFrameAsync(frame);
                break;

            case FrameData:
                HandleDataFrame(frame);
                break;

            case FrameRstStream:
                _streams.TryRemove(frame.StreamId, out _);
                break;

            case FrameGoaway:
                // Client is done; stop accepting new streams
                break;

            case FramePriority:
                // Ignore priority — not implemented
                break;
        }
    }

    private async ValueTask HandleHeadersFrameAsync(Http2Frame frame)
    {
        var payload = frame.Payload.AsSpan();
        int padLen = 0;
        if ((frame.Flags & FlagPadded) != 0) { padLen = payload[0]; payload = payload[1..]; }
        if ((frame.Flags & FlagPriority) != 0) payload = payload[5..]; // skip priority block

        if (padLen > 0) payload = payload[..^padLen];

        // Accumulate header block; may span multiple CONTINUATION frames (RFC 7540 §6.10)
        var headerBlock = payload.ToArray();

        bool endHeaders = (frame.Flags & FlagEndHeaders) != 0;
        bool endStream  = (frame.Flags & FlagEndStream)  != 0;

        var stream = _streams.GetOrAdd(frame.StreamId, id => new Http2Stream(id));
        stream.HeaderBlock.AddRange(headerBlock);

        // Accumulate CONTINUATION frames until END_HEADERS is set (RFC 7540 §6.10)
        while (!endHeaders)
        {
            var contFrame = await ReadFrameAsync();
            if (contFrame is null || contFrame.Type != FrameContinuation || contFrame.StreamId != frame.StreamId)
            {
                // Protocol error: expected CONTINUATION on the same stream
                await SendGoAwayAsync(frame.StreamId, ErrProtocolError);
                return;
            }
            stream.HeaderBlock.AddRange(contFrame.Payload);
            endHeaders = (contFrame.Flags & FlagEndHeaders) != 0;
        }

        var decodedHeaders = _hpack.Decode(stream.HeaderBlock.ToArray());
        stream.Headers = decodedHeaders.Select(h => new HeaderEntry(
            new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(h.name)),
            new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(h.value)))).ToList();
        stream.HeadersComplete = true;

        if (endStream)
        {
            // No body — dispatch immediately
            _ = RunStreamAsync(stream);
        }

        // Send connection WINDOW_UPDATE to allow client to send more data
        await SendWindowUpdateAsync(0, InitialWindowSize);
    }

    private void HandleDataFrame(Http2Frame frame)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var stream)) return;

        bool endStream = (frame.Flags & FlagEndStream) != 0;
        var payload = frame.Payload.AsSpan();

        // Handle padding
        if ((frame.Flags & FlagPadded) != 0)
        {
            int pad = payload[0];
            payload = payload[1..^pad];
        }

        stream.BodySegments.Add(payload.ToArray());

        if (endStream)
        {
            _ = RunStreamAsync(stream);
        }
    }

    // ── Per-stream request handler ─────────────────────────────────────────

    private async Task RunStreamAsync(Http2Stream stream)
    {
        var httpContext = HttpContextPool.Rent();
        try
        {
            PopulateContext(httpContext, stream);

            try { await _pipeline(httpContext); }
            catch (Exception ex)
            {
                if (!httpContext.Response.IsStarted)
                {
                    httpContext.Response.StatusCode = 500;
                    httpContext.Response.WriteText($"Internal Server Error: {ex.Message}");
                }
            }

            await SendResponseAsync(stream.StreamId, httpContext.Response);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[H2 Stream {StreamId}] {ExType}", stream.StreamId, ex.GetType().Name);
            if (_logger is null) Console.Error.WriteLine($"[H2 Stream {stream.StreamId}] {ex.GetType().Name}: {ex.Message}");
            await SendRstStreamAsync(stream.StreamId, ErrStreamClosed);
        }
        finally
        {
            // Always remove the stream from active tracking and clean up resources
            _streams.TryRemove(stream.StreamId, out _);
            httpContext._disposeScope?.Dispose();
            HttpContextPool.Return(httpContext);
        }
    }

    private void PopulateContext(HttpContext ctx, Http2Stream stream)
    {
        string method = "", path = "", scheme = "https", authority = "";
        var appHeaders = new List<HeaderEntry>();

        foreach (var h in stream.Headers)
        {
            // For HTTP/2, name comparison is still string-based here for simplicity in this implementation
            switch (h.Name)
            {
                case ":method":    method    = h.Value; break;
                case ":path":      path      = h.Value; break;
                case ":scheme":    scheme    = h.Value; break;
                case ":authority": authority = h.Value; break;
                default:           appHeaders.Add(h); break;
            }
        }

        string queryString = string.Empty;
        int qIdx = path.IndexOf('?');
        if (qIdx >= 0) { queryString = path[(qIdx + 1)..]; path = path[..qIdx]; }

        Http.HttpMethod httpMethod;
        try { httpMethod = HttpMethodExtensions.Parse(method); }
        catch { httpMethod = Http.HttpMethod.GET; }

        // Flatten body segments (avoid LINQ enumerator allocation on hot path)
        int totalLen = 0;
        foreach (var seg in stream.BodySegments) totalLen += seg.Length;
        byte[] body = new byte[totalLen];
        int offset = 0;
        foreach (var seg in stream.BodySegments) { seg.CopyTo(body, offset); offset += seg.Length; }

        // materialization for HTTP/2 simple dictionary
        var headersDict = new Dictionary<string, string>(appHeaders.Count + 1, StringComparer.OrdinalIgnoreCase);
        if (authority.Length > 0) headersDict["Host"] = authority;
        foreach (var h in appHeaders) headersDict[h.Name] = h.Value;

        ctx.Request.Method = httpMethod;
        ctx.Request.Path = path;
        ctx.Request.QueryString = queryString;
        ctx.Request.Headers = headersDict;
        ctx.Request.Query = queryString.Length == 0
                ? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(0)
                : ParseQuery(queryString);
        ctx.Request.Body = body;

        // Populate well-known headers for H2
        if (headersDict.TryGetValue("Content-Length", out var cl) && long.TryParse(cl, out var clVal)) ctx.Request.ContentLength = clVal;
        if (headersDict.TryGetValue("Content-Type", out var ct)) ctx.Request.ContentType = ct;
        if (headersDict.TryGetValue("Host", out var host)) ctx.Request.Host = host;
        if (headersDict.TryGetValue("Authorization", out var auth)) ctx.Request.Authorization = auth;

        ctx.Initialize(_services, _ct);
        var scope = new Http11Connection.LazyScopeProvider(_services);
        ctx._disposeScope = scope;
    }

    private static Dictionary<string, string> ParseQuery(string qs)
    {
        var d = new Dictionary<string, string>(4, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq < 0) d[WebUtility.UrlDecode(pair)] = string.Empty;
            else d[WebUtility.UrlDecode(pair[..eq])] = WebUtility.UrlDecode(pair[(eq + 1)..]);
        }
        return d;
    }

    // ── Response sending ──────────────────────────────────────────────────

    private async ValueTask SendResponseAsync(int streamId, HttpResponse response)
    {
        // Build headers block
        var responseHeaders = new Dictionary<string, string>(response.Headers.Count + 1,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in response.Headers)
            responseHeaders[name.ToLowerInvariant()] = value;
        if (!responseHeaders.ContainsKey("content-type") && response.Body.Length > 0)
            responseHeaders["content-type"] = "text/plain";
        if (_altSvcValue is not null && !responseHeaders.ContainsKey("alt-svc"))
            responseHeaders["alt-svc"] = _altSvcValue;

        var headersBlock = HpackEncoder.EncodeResponse(response.StatusCode, responseHeaders, response.SetCookieHeaders);

        bool hasBody = response.Body.Length > 0;

        await _writeLock.WaitAsync(_ct);
        try
        {
            // HEADERS frame
            byte headersFlags = FlagEndHeaders;
            if (!hasBody) headersFlags |= FlagEndStream;
            WriteFrameHeader(_writer, headersBlock.Length, FrameHeaders, headersFlags, streamId);
            _writer.Write(headersBlock);

            // DATA frame
            if (hasBody)
            {
                WriteFrameHeader(_writer, response.Body.Length, FrameData, FlagEndStream, streamId);
                _writer.Write(response.Body);
            }

            await _writer.FlushAsync(_ct);
        }
        finally { _writeLock.Release(); }
    }

    // ── Frame writers ─────────────────────────────────────────────────────

    private async ValueTask SendSettingsAsync()
    {
        await _writeLock.WaitAsync(_ct);
        try
        {
            // Empty SETTINGS frame (all defaults)
            WriteFrameHeader(_writer, 0, FrameSettings, 0, 0);
            await _writer.FlushAsync(_ct);
        }
        finally { _writeLock.Release(); }
    }

    private async ValueTask SendSettingsAckAsync()
    {
        await _writeLock.WaitAsync(_ct);
        try
        {
            WriteFrameHeader(_writer, 0, FrameSettings, FlagAck, 0);
            await _writer.FlushAsync(_ct);
        }
        finally { _writeLock.Release(); }
    }

    private async ValueTask SendPingAckAsync(byte[] pingPayload)
    {
        await _writeLock.WaitAsync(_ct);
        try
        {
            WriteFrameHeader(_writer, 8, FramePing, FlagAck, 0);
            _writer.Write(pingPayload.AsSpan(0, Math.Min(8, pingPayload.Length)));
            await _writer.FlushAsync(_ct);
        }
        finally { _writeLock.Release(); }
    }

    private async ValueTask SendWindowUpdateAsync(int streamId, int increment)
    {
        await _writeLock.WaitAsync(_ct);
        try
        {
            WriteFrameHeader(_writer, 4, FrameWindowUpdate, 0, streamId);
            Span<byte> inc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(inc, (uint)increment);
            _writer.Write(inc);
            await _writer.FlushAsync(_ct);
        }
        finally { _writeLock.Release(); }
    }

    private async ValueTask SendGoAwayAsync(int lastStreamId, uint errorCode)
    {
        await _writeLock.WaitAsync(_ct);
        try
        {
            WriteFrameHeader(_writer, 8, FrameGoaway, 0, 0);
            Span<byte> payload = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)lastStreamId & 0x7FFFFFFFu);
            BinaryPrimitives.WriteUInt32BigEndian(payload[4..], errorCode);
            _writer.Write(payload);
            await _writer.FlushAsync(_ct);
        }
        finally { _writeLock.Release(); }
    }

    private async ValueTask SendRstStreamAsync(int streamId, uint errorCode)    {
        await _writeLock.WaitAsync(_ct);
        try
        {
            WriteFrameHeader(_writer, 4, FrameRstStream, 0, streamId);
            Span<byte> code = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(code, errorCode);
            _writer.Write(code);
            await _writer.FlushAsync(_ct);
        }
        finally { _writeLock.Release(); }
    }

    private static void WriteFrameHeader(PipeWriter w, int length, byte type, byte flags, int streamId)
    {
        Span<byte> hdr = stackalloc byte[9];
        hdr[0] = (byte)(length >> 16);
        hdr[1] = (byte)(length >> 8);
        hdr[2] = (byte)length;
        hdr[3] = type;
        hdr[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(hdr[5..], (uint)streamId & 0x7FFFFFFFu);
        w.Write(hdr);
    }

    // ── Internal types ────────────────────────────────────────────────────

    private sealed class Http2Frame(byte type, byte flags, int streamId, byte[] payload)
    {
        public byte   Type     => type;
        public byte   Flags    => flags;
        public int    StreamId => streamId;
        public byte[] Payload  => payload;
    }

    private sealed class Http2Stream(int id)
    {
        public int      StreamId        => id;
        public List<byte> HeaderBlock   = new();
        public List<HeaderEntry> Headers = new();
        public bool HeadersComplete;
        public List<byte[]> BodySegments = new();
    }
}
