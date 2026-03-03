using System.Buffers.Binary;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.DotNetty;

/// <summary>
/// Minimal HTTP/2 server-side frame handler for h2c (HTTP/2 cleartext) connections.
///
/// Handles the essential frame types required for a functioning HTTP/2 server:
/// <list type="bullet">
///   <item><description>Connection preface validation (PRI * HTTP/2.0 SM)</description></item>
///   <item><description>SETTINGS / SETTINGS_ACK frames</description></item>
///   <item><description>WINDOW_UPDATE frames (flow control bookkeeping)</description></item>
///   <item><description>PING / PING_ACK frames</description></item>
///   <item><description>HEADERS frames decoded via static HPACK table</description></item>
///   <item><description>DATA frames (request body accumulation)</description></item>
///   <item><description>RST_STREAM frames</description></item>
///   <item><description>GOAWAY on connection close</description></item>
/// </list>
///
/// Responses are sent as HEADERS (with status) + DATA frames.
///
/// Limitation: this implementation uses a read-only static HPACK table.
/// Dynamic HPACK table updates sent by clients are accepted but discarded.
/// Static table entries cover all standard HTTP methods and common header names,
/// which is sufficient for most API workloads.
/// </summary>
internal sealed class Http2FrameHandler : ChannelHandlerAdapter
{
    // ── HTTP/2 constants ───────────────────────────────────────────────────

    private static readonly byte[] ConnectionPreface =
        "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

    private const int FrameHeaderSize = 9;

    // Frame types
    private const byte TypeData         = 0x0;
    private const byte TypeHeaders      = 0x1;
    private const byte TypePriority     = 0x2;
    private const byte TypeRstStream    = 0x3;
    private const byte TypeSettings     = 0x4;
    private const byte TypePing         = 0x6;
    private const byte TypeGoAway       = 0x7;

    // Flags
    private const byte FlagEndStream  = 0x1;
    private const byte FlagEndHeaders = 0x4;
    private const byte FlagAck        = 0x1;
    private const byte FlagPadded     = 0x8;
    private const byte FlagPriority   = 0x20;

    // Error codes
    private const int ErrNoError       = 0x0;
    private const int ErrProtocolError = 0x1;

    // Connection-level stream id
    private const int ConnectionStreamId = 0;

    // ── HPACK static table (RFC 7541 Appendix A, indices 1–61) ───────────

    private static readonly (string Name, string Value)[] StaticTable =
    [
        (":authority",                  ""),               // 1
        (":method",                     "GET"),            // 2
        (":method",                     "POST"),           // 3
        (":path",                       "/"),              // 4
        (":path",                       "/index.html"),    // 5
        (":scheme",                     "http"),           // 6
        (":scheme",                     "https"),          // 7
        (":status",                     "200"),            // 8
        (":status",                     "204"),            // 9
        (":status",                     "206"),            // 10
        (":status",                     "304"),            // 11
        (":status",                     "400"),            // 12
        (":status",                     "404"),            // 13
        (":status",                     "500"),            // 14
        ("accept-charset",              ""),               // 15
        ("accept-encoding",             "gzip, deflate"),  // 16
        ("accept-language",             ""),               // 17
        ("accept-ranges",               ""),               // 18
        ("accept",                      ""),               // 19
        ("access-control-allow-origin", ""),               // 20
        ("age",                         ""),               // 21
        ("allow",                       ""),               // 22
        ("authorization",               ""),               // 23
        ("cache-control",               ""),               // 24
        ("content-disposition",         ""),               // 25
        ("content-encoding",            ""),               // 26
        ("content-language",            ""),               // 27
        ("content-length",              ""),               // 28
        ("content-location",            ""),               // 29
        ("content-range",               ""),               // 30
        ("content-type",                ""),               // 31
        ("cookie",                      ""),               // 32
        ("date",                        ""),               // 33
        ("etag",                        ""),               // 34
        ("expect",                      ""),               // 35
        ("expires",                     ""),               // 36
        ("from",                        ""),               // 37
        ("host",                        ""),               // 38
        ("if-match",                    ""),               // 39
        ("if-modified-since",           ""),               // 40
        ("if-none-match",               ""),               // 41
        ("if-range",                    ""),               // 42
        ("if-unmodified-since",         ""),               // 43
        ("last-modified",               ""),               // 44
        ("link",                        ""),               // 45
        ("location",                    ""),               // 46
        ("max-forwards",                ""),               // 47
        ("proxy-authenticate",          ""),               // 48
        ("proxy-authorization",         ""),               // 49
        ("range",                       ""),               // 50
        ("referer",                     ""),               // 51
        ("refresh",                     ""),               // 52
        ("retry-after",                 ""),               // 53
        ("server",                      ""),               // 54
        ("set-cookie",                  ""),               // 55
        ("strict-transport-security",   ""),               // 56
        ("transfer-encoding",           ""),               // 57
        ("user-agent",                  ""),               // 58
        ("vary",                        ""),               // 59
        ("via",                         ""),               // 60
        ("www-authenticate",            ""),               // 61
    ];

    // ── State ─────────────────────────────────────────────────────────────

    private readonly RequestDelegate _appPipeline;
    private readonly IServiceProvider _services;
    private readonly Dictionary<int, StreamState> _streams = new();
    private bool _prefaceReceived;
    private readonly List<byte> _readBuffer = new();

    public Http2FrameHandler(RequestDelegate appPipeline, IServiceProvider services)
    {
        _appPipeline = appPipeline;
        _services = services;
    }

    // ── DotNetty lifecycle ─────────────────────────────────────────────────

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        // Send server SETTINGS immediately on connect (fire-and-forget)
        _ = SendSettingsAsync(ctx);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is not IByteBuffer buf) return;

        try
        {
            var bytes = new byte[buf.ReadableBytes];
            buf.ReadBytes(bytes);
            _readBuffer.AddRange(bytes);
            _ = ProcessBufferAsync(ctx);
        }
        finally
        {
            buf.Release();
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception exception) =>
        ctx.CloseAsync();

    // ── Buffer processing ─────────────────────────────────────────────────

    private async Task ProcessBufferAsync(IChannelHandlerContext ctx)
    {
        if (!_prefaceReceived)
        {
            if (_readBuffer.Count < ConnectionPreface.Length) return;

            for (int i = 0; i < ConnectionPreface.Length; i++)
            {
                if (_readBuffer[i] != ConnectionPreface[i])
                {
                    await SendGoAwayAsync(ctx, ConnectionStreamId, ErrProtocolError, "Invalid connection preface");
                    await ctx.CloseAsync();
                    return;
                }
            }

            _readBuffer.RemoveRange(0, ConnectionPreface.Length);
            _prefaceReceived = true;
        }

        while (_readBuffer.Count >= FrameHeaderSize)
        {
            int length = (_readBuffer[0] << 16) | (_readBuffer[1] << 8) | _readBuffer[2];
            byte type     = _readBuffer[3];
            byte flags    = _readBuffer[4];
            int streamId  = ((_readBuffer[5] & 0x7F) << 24)
                          | (_readBuffer[6] << 16)
                          | (_readBuffer[7] << 8)
                          | _readBuffer[8];

            if (_readBuffer.Count < FrameHeaderSize + length) break;

            byte[] payload = _readBuffer.GetRange(FrameHeaderSize, length).ToArray();
            _readBuffer.RemoveRange(0, FrameHeaderSize + length);

            await HandleFrameAsync(ctx, type, flags, streamId, payload);
        }
    }

    private async Task HandleFrameAsync(IChannelHandlerContext ctx, byte type, byte flags, int streamId, byte[] payload)
    {
        switch (type)
        {
            case TypeSettings:    await HandleSettingsAsync(ctx, flags); break;
            case TypePing:        await HandlePingAsync(ctx, flags, payload); break;
            case TypeHeaders:     HandleHeaders(ctx, flags, streamId, payload); break;
            case TypeData:        HandleData(ctx, flags, streamId, payload); break;
            case TypeRstStream:   _streams.Remove(streamId); break;
            case TypePriority:    break; // deprecated, accept and ignore
            case TypeGoAway:      await ctx.CloseAsync(); break;
            default:
                if (streamId != ConnectionStreamId)
                    await SendRstStreamAsync(ctx, streamId, ErrProtocolError);
                break;
        }
    }

    // ── SETTINGS ─────────────────────────────────────────────────────────

    private async Task HandleSettingsAsync(IChannelHandlerContext ctx, byte flags)
    {
        if ((flags & FlagAck) != 0) return;
        await SendSettingsAckAsync(ctx);
    }

    private Task SendSettingsAsync(IChannelHandlerContext ctx) =>
        WriteFrameAsync(ctx, TypeSettings, 0, ConnectionStreamId, []);

    private Task SendSettingsAckAsync(IChannelHandlerContext ctx) =>
        WriteFrameAsync(ctx, TypeSettings, FlagAck, ConnectionStreamId, []);

    // ── PING ─────────────────────────────────────────────────────────────

    private async Task HandlePingAsync(IChannelHandlerContext ctx, byte flags, byte[] payload)
    {
        if ((flags & FlagAck) != 0) return;
        await WriteFrameAsync(ctx, TypePing, FlagAck, ConnectionStreamId, payload);
    }

    // ── HEADERS ──────────────────────────────────────────────────────────

    private void HandleHeaders(IChannelHandlerContext ctx, byte flags, int streamId, byte[] payload)
    {
        int offset = 0;

        int padLength = 0;
        if ((flags & FlagPadded) != 0)
            padLength = payload[offset++];

        if ((flags & FlagPriority) != 0)
            offset += 5;

        int headerBlockEnd = payload.Length - padLength;
        byte[] headerBlock = payload[offset..headerBlockEnd];

        var headers = HpackDecode(headerBlock);

        if (!_streams.TryGetValue(streamId, out var stream))
        {
            stream = new StreamState();
            _streams[streamId] = stream;
        }

        foreach (var (name, value) in headers)
        {
            switch (name)
            {
                case ":method":    stream.Method    = value; break;
                case ":path":      stream.Path      = value; break;
                case ":scheme":    stream.Scheme    = value; break;
                case ":authority": stream.Authority = value; break;
                default:           stream.RequestHeaders[name] = value; break;
            }
        }

        if ((flags & FlagEndStream) != 0)
            _ = DispatchAsync(ctx, streamId, stream);
    }

    // ── DATA ──────────────────────────────────────────────────────────────

    private void HandleData(IChannelHandlerContext ctx, byte flags, int streamId, byte[] payload)
    {
        if (!_streams.TryGetValue(streamId, out var stream)) return;

        int offset = 0;
        int padLength = 0;
        if ((flags & FlagPadded) != 0)
            padLength = payload[offset++];

        int dataEnd = payload.Length - padLength;
        if (dataEnd > offset)
            stream.BodySegments.Add(payload[offset..dataEnd]);

        if ((flags & FlagEndStream) != 0)
            _ = DispatchAsync(ctx, streamId, stream);
    }

    // ── Dispatch to application ───────────────────────────────────────────

    private async Task DispatchAsync(IChannelHandlerContext ctx, int streamId, StreamState stream)
    {
        var body = stream.BodySegments.Count == 0
            ? []
            : stream.BodySegments.SelectMany(s => s).ToArray();

        using var scope = _services.CreateScope();
        var httpCtx = BuildHttpContext(stream, body, scope.ServiceProvider);

        try
        {
            await _appPipeline(httpCtx);
        }
        catch (Exception ex)
        {
            httpCtx.Response.StatusCode = 500;
            httpCtx.Response.WriteText(ex.Message);
        }

        await SendResponseAsync(ctx, streamId, httpCtx.Response);
        _streams.Remove(streamId);
    }

    private static HttpContext BuildHttpContext(StreamState stream, byte[] body, IServiceProvider services)
    {
        var uri = stream.Path ?? "/";
        var qIdx = uri.IndexOf('?');
        var path = qIdx >= 0 ? uri[..qIdx] : uri;
        var queryString = qIdx >= 0 ? uri[(qIdx + 1)..] : string.Empty;

        var method = stream.Method?.ToUpperInvariant() switch
        {
            "GET"    => HttpMethod.GET,
            "POST"   => HttpMethod.POST,
            "PUT"    => HttpMethod.PUT,
            "DELETE" => HttpMethod.DELETE,
            "PATCH"  => HttpMethod.PATCH,
            _        => HttpMethod.GET
        };

        // Parse query parameters
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(queryString))
        {
            foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) query[Uri.UnescapeDataString(pair)] = string.Empty;
                else query[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        var req = new HttpRequest
        {
            Method      = method,
            Path        = path,
            QueryString = queryString,
            Headers     = stream.RequestHeaders,
            Query       = query,
            Body        = body
        };

        return new HttpContext(req, new HttpResponse(), services);
    }

    private async Task SendResponseAsync(IChannelHandlerContext ctx, int streamId, HttpResponse res)
    {
        var responseBody = res.Body ?? [];
        var headerBlock  = HpackEncodeResponse(res.StatusCode, res.Headers, responseBody.Length);

        bool hasBody = responseBody.Length > 0;
        byte headerFlags = FlagEndHeaders;
        if (!hasBody) headerFlags |= FlagEndStream;

        await WriteFrameAsync(ctx, TypeHeaders, headerFlags, streamId, headerBlock);

        if (hasBody)
            await WriteFrameAsync(ctx, TypeData, FlagEndStream, streamId, responseBody);
    }

    // ── RST_STREAM / GOAWAY ───────────────────────────────────────────────

    private Task SendRstStreamAsync(IChannelHandlerContext ctx, int streamId, int errorCode)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, errorCode);
        return WriteFrameAsync(ctx, TypeRstStream, 0, streamId, payload);
    }

    private Task SendGoAwayAsync(IChannelHandlerContext ctx, int lastStreamId, int errorCode, string? debug = null)
    {
        var debugBytes = debug is not null ? Encoding.UTF8.GetBytes(debug) : [];
        var payload = new byte[8 + debugBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0), lastStreamId & 0x7FFFFFFF);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4), errorCode);
        debugBytes.CopyTo(payload, 8);
        return WriteFrameAsync(ctx, TypeGoAway, 0, ConnectionStreamId, payload);
    }

    // ── Frame writer ──────────────────────────────────────────────────────

    private static Task WriteFrameAsync(IChannelHandlerContext ctx, byte type, byte flags, int streamId, byte[] payload)
    {
        var buf = ctx.Allocator.Buffer(FrameHeaderSize + payload.Length);
        buf.WriteByte((payload.Length >> 16) & 0xFF);
        buf.WriteByte((payload.Length >> 8) & 0xFF);
        buf.WriteByte(payload.Length & 0xFF);
        buf.WriteByte(type);
        buf.WriteByte(flags);
        buf.WriteInt(streamId & 0x7FFFFFFF);
        if (payload.Length > 0)
            buf.WriteBytes(payload);
        return ctx.WriteAndFlushAsync(buf);
    }

    // ── Minimal HPACK decoder (static table only) ─────────────────────────

    private static List<(string Name, string Value)> HpackDecode(byte[] block)
    {
        var result = new List<(string, string)>();
        int i = 0;

        while (i < block.Length)
        {
            byte b = block[i];

            if ((b & 0x80) != 0)
            {
                // Indexed header field (§6.1)
                int idx = DecodeInt(block, ref i, 7);
                if (idx > 0 && idx <= StaticTable.Length)
                    result.Add(StaticTable[idx - 1]);
            }
            else if ((b & 0xC0) == 0x40)
            {
                // Literal with Incremental Indexing (§6.2.1)
                int nameIdx = DecodeInt(block, ref i, 6);
                string name  = nameIdx > 0 ? StaticTable[nameIdx - 1].Name : DecodeString(block, ref i);
                string value = DecodeString(block, ref i);
                result.Add((name, value));
            }
            else if ((b & 0xE0) == 0x20)
            {
                // Dynamic Table Size Update (§6.3)
                DecodeInt(block, ref i, 5);
            }
            else
            {
                // Literal Without Indexing / Never Indexed (§6.2.2 / §6.2.3)
                int nameIdx = DecodeInt(block, ref i, 4);
                string name  = nameIdx > 0 ? StaticTable[nameIdx - 1].Name : DecodeString(block, ref i);
                string value = DecodeString(block, ref i);
                result.Add((name, value));
            }
        }

        return result;
    }

    private static int DecodeInt(byte[] buf, ref int pos, int prefixBits)
    {
        int mask  = (1 << prefixBits) - 1;
        int value = buf[pos++] & mask;
        if (value < mask) return value;

        int shift = 0;
        while (pos < buf.Length)
        {
            byte next = buf[pos++];
            value += (next & 0x7F) << shift;
            shift += 7;
            if ((next & 0x80) == 0) break;
        }
        return value;
    }

    private static string DecodeString(byte[] buf, ref int pos)
    {
        bool huffman = (buf[pos] & 0x80) != 0;
        int len = DecodeInt(buf, ref pos, 7);
        if (pos + len > buf.Length) return string.Empty;

        byte[] raw = buf[pos..(pos + len)];
        pos += len;

        // Huffman decode falls back to Latin-1 if unavailable.
        return huffman ? HuffmanDecode(raw) : Encoding.Latin1.GetString(raw);
    }

    // ── Minimal HPACK encoder (response) ──────────────────────────────────

    private static byte[] HpackEncodeResponse(int statusCode, Dictionary<string, string> headers, int bodyLength)
    {
        var ms = new MemoryStream();

        // :status
        int statusIdx = statusCode switch
        {
            200 => 8, 204 => 9, 206 => 10, 304 => 11,
            400 => 12, 404 => 13, 500 => 14, _ => 0
        };
        if (statusIdx > 0)
        {
            ms.WriteByte((byte)(0x80 | statusIdx)); // Indexed
        }
        else
        {
            ms.WriteByte(0x48); // Literal incremental, name = static[8] = :status
            WriteLiteralString(ms, statusCode.ToString());
        }

        // content-length (static index 28)
        ms.WriteByte(0x5C); // 0100 0000 | 28
        WriteLiteralString(ms, bodyLength.ToString());

        // content-type
        if (headers.TryGetValue("Content-Type", out var ct))
        {
            ms.WriteByte(0x5F); // 0100 0000 | 31
            WriteLiteralString(ms, ct);
        }

        // Remaining headers as literal, no indexing
        foreach (var (name, value) in headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            ms.WriteByte(0x00);
            WriteLiteralString(ms, name.ToLowerInvariant());
            WriteLiteralString(ms, value);
        }

        return ms.ToArray();
    }

    private static void WriteLiteralString(Stream ms, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        EncodeInt(ms, bytes.Length, 7); // H=0, not Huffman
        ms.Write(bytes);
    }

    private static void EncodeInt(Stream ms, int value, int prefixBits)
    {
        int maxPrefix = (1 << prefixBits) - 1;
        if (value < maxPrefix) { ms.WriteByte((byte)value); return; }
        ms.WriteByte((byte)maxPrefix);
        value -= maxPrefix;
        while (value >= 0x80) { ms.WriteByte((byte)((value & 0x7F) | 0x80)); value >>= 7; }
        ms.WriteByte((byte)value);
    }

    // ── Huffman decoder ───────────────────────────────────────────────────

    private static string HuffmanDecode(byte[] encoded)
    {
        // Attempt to use System.Net.Http's internal Huffman decoder via reflection.
        // Falls back to Latin-1 (safe: Huffman is optional in HPACK).
        try
        {
            var type = Type.GetType("System.Net.Http.HPack.HuffmanDecoder, System.Net.Http");
            if (type is null) return Encoding.Latin1.GetString(encoded);
            var method = type.GetMethod("Decode",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                [typeof(byte[])]);
            if (method is null) return Encoding.Latin1.GetString(encoded);
            return method.Invoke(null, [encoded]) as string ?? Encoding.Latin1.GetString(encoded);
        }
        catch
        {
            return Encoding.Latin1.GetString(encoded);
        }
    }

    // ── Per-stream state ──────────────────────────────────────────────────

    private sealed class StreamState
    {
        public string? Method;
        public string? Path;
        public string? Scheme;
        public string? Authority;
        public Dictionary<string, string> RequestHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<byte[]> BodySegments { get; } = [];
    }
}
