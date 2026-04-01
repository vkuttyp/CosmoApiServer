using System.Buffers;
using System.Net;
using System.Net.Quic;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Experimental HTTP/3 transport with a minimal request/response path for buffered handlers.
/// This currently supports basic request streams with a HEADERS frame followed by optional DATA.
/// Dynamic QPACK tables, trailers, push, and advanced HTTP/3 control handling are not implemented.
/// </summary>
internal static class Http3Connection
{
    private const long Http3GeneralProtocolError = 0x0101;
    private const long Http3InternalError = 0x0102;

    private const long FrameData = 0x00;
    private const long FrameHeaders = 0x01;
    private const long FrameSettings = 0x04;

    private const long StreamTypeControl = 0x00;
    private const long StreamTypeQpackEncoder = 0x02;
    private const long StreamTypeQpackDecoder = 0x03;

    private const long SettingsQpackMaxTableCapacity = 0x01;
    private const long SettingsQpackBlockedStreams = 0x07;

    private static readonly (string name, string value)[] QpackStaticTable =
    [
        (":authority", ""),
        (":path", "/"),
        ("age", "0"),
        ("content-disposition", ""),
        ("content-length", "0"),
        ("cookie", ""),
        ("date", ""),
        ("etag", ""),
        ("if-modified-since", ""),
        ("if-none-match", ""),
        ("last-modified", ""),
        ("link", ""),
        ("location", ""),
        ("referer", ""),
        ("set-cookie", ""),
        (":method", "CONNECT"),
        (":method", "DELETE"),
        (":method", "GET"),
        (":method", "HEAD"),
        (":method", "OPTIONS"),
        (":method", "POST"),
        (":method", "PUT"),
        (":scheme", "http"),
        (":scheme", "https"),
        (":status", "103"),
        (":status", "200"),
        (":status", "304"),
        (":status", "404"),
        (":status", "503"),
        ("accept", "*/*"),
        ("accept", "application/dns-message"),
        ("accept-encoding", "gzip, deflate, br"),
        ("accept-ranges", "bytes"),
        ("access-control-allow-headers", "cache-control"),
        ("access-control-allow-headers", "content-type"),
        ("access-control-allow-origin", "*"),
        ("cache-control", "max-age=0"),
        ("cache-control", "max-age=2592000"),
        ("cache-control", "max-age=604800"),
        ("cache-control", "no-cache"),
        ("cache-control", "no-store"),
        ("cache-control", "public, max-age=31536000"),
        ("content-encoding", "br"),
        ("content-encoding", "gzip"),
        ("content-type", "application/dns-message"),
        ("content-type", "application/javascript"),
        ("content-type", "application/json"),
        ("content-type", "application/x-www-form-urlencoded"),
        ("content-type", "image/gif"),
        ("content-type", "image/jpeg"),
        ("content-type", "image/png"),
        ("content-type", "text/css"),
        ("content-type", "text/html; charset=utf-8"),
        ("content-type", "text/plain"),
        ("content-type", "text/plain;charset=utf-8"),
        ("range", "bytes=0-"),
        ("strict-transport-security", "max-age=31536000"),
        ("strict-transport-security", "max-age=31536000; includesubdomains"),
        ("strict-transport-security", "max-age=31536000; includesubdomains; preload"),
        ("vary", "accept-encoding"),
        ("vary", "origin"),
        ("x-content-type-options", "nosniff"),
        ("x-xss-protection", "1; mode=block"),
        (":status", "100"),
        (":status", "204"),
        (":status", "206"),
        (":status", "302"),
        (":status", "400"),
        (":status", "403"),
        (":status", "421"),
        (":status", "425"),
        (":status", "500"),
        ("accept-language", ""),
        ("access-control-allow-credentials", "FALSE"),
        ("access-control-allow-credentials", "TRUE"),
        ("access-control-allow-headers", "*"),
        ("access-control-allow-methods", "get"),
        ("access-control-allow-methods", "get, post, options"),
        ("access-control-allow-methods", "options"),
        ("access-control-expose-headers", "content-length"),
        ("access-control-request-headers", "content-type"),
        ("access-control-request-method", "get"),
        ("access-control-request-method", "post"),
        ("alt-svc", "clear"),
        ("authorization", ""),
        ("content-security-policy", "script-src 'none'; object-src 'none'; base-uri 'none'"),
        ("early-data", "1"),
        ("expect-ct", ""),
        ("forwarded", ""),
        ("if-range", ""),
        ("origin", ""),
        ("purpose", "prefetch"),
        ("server", ""),
        ("timing-allow-origin", "*"),
        ("upgrade-insecure-requests", "1"),
        ("user-agent", ""),
        ("x-forwarded-for", ""),
        ("x-frame-options", "deny"),
        ("x-frame-options", "sameorigin"),
    ];

    private static bool SupportsQuicPlatform() =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    public static async ValueTask RunAsync(
        QuicConnection connection,
        RequestDelegate pipeline,
        IServiceProvider services,
        CancellationToken ct)
    {
        if (!SupportsQuicPlatform() || !QuicConnection.IsSupported)
            return;

#pragma warning disable CA1416
        var remoteIp = (connection.RemoteEndPoint as IPEndPoint)?.Address.ToString();

        await InitializeServerStreamsAsync(connection, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var stream = await connection.AcceptInboundStreamAsync(ct);
                _ = stream.Type == QuicStreamType.Bidirectional
                    ? HandleRequestStreamAsync(stream, pipeline, services, remoteIp, ct)
                    : HandleUnidirectionalStreamAsync(stream, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (QuicException) { }
#pragma warning restore CA1416
    }

    private static async Task InitializeServerStreamsAsync(QuicConnection connection, CancellationToken ct)
    {
#pragma warning disable CA1416
        var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, ct);
        var encoder = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, ct);
        var decoder = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, ct);

        await WriteVarIntAsync(control, StreamTypeControl, ct);

        using var settingsPayload = new MemoryStream();
        WriteVarInt(settingsPayload, SettingsQpackMaxTableCapacity);
        WriteVarInt(settingsPayload, 0);
        WriteVarInt(settingsPayload, SettingsQpackBlockedStreams);
        WriteVarInt(settingsPayload, 0);
        await WriteFrameAsync(control, FrameSettings, settingsPayload.ToArray(), false, ct);

        await WriteVarIntAsync(encoder, StreamTypeQpackEncoder, ct);
        await encoder.FlushAsync(ct);

        await WriteVarIntAsync(decoder, StreamTypeQpackDecoder, ct);
        await decoder.FlushAsync(ct);
#pragma warning restore CA1416
    }

    private static async Task HandleUnidirectionalStreamAsync(QuicStream stream, CancellationToken ct)
    {
#pragma warning disable CA1416
        try
        {
            long streamType = await ReadVarIntAsync(stream, ct);
            switch (streamType)
            {
                case StreamTypeControl:
                    await ConsumeControlStreamAsync(stream, ct);
                    break;
                case StreamTypeQpackEncoder:
                case StreamTypeQpackDecoder:
                    await DrainStreamAsync(stream, ct);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported HTTP/3 unidirectional stream type: {streamType}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            await stream.DisposeAsync();
        }
#pragma warning restore CA1416
    }

    private static async Task HandleRequestStreamAsync(
        QuicStream stream,
        RequestDelegate pipeline,
        IServiceProvider services,
        string? remoteIp,
        CancellationToken ct)
    {
#pragma warning disable CA1416
        var httpContext = HttpContextPool.Rent();
        try
        {
            var requestHead = await ReadRequestHeadAsync(stream, ct);
            PopulateHttpContext(httpContext, requestHead, new Http3RequestBodyStream(stream), services, remoteIp, ct);
            bool headOnly = requestHead.Method == CosmoApiServer.Core.Http.HttpMethod.HEAD;
            httpContext.Response.StreamingResponseWriter =
                (statusCode, bodyWriter, writeCt) => WriteStreamingResponseAsync(stream, httpContext.Response, headOnly, statusCode, bodyWriter, writeCt);

            try
            {
                await pipeline(httpContext);
            }
            catch (Exception ex)
            {
                httpContext.Response.StatusCode = 500;
                httpContext.Response.WriteText($"Internal Server Error: {ex.Message}");
            }
            finally
            {
                if (httpContext.Request.BodyStream is Http3RequestBodyStream bodyStream)
                    await bodyStream.DrainAsync(ct);
                httpContext._disposeScope?.Dispose();
            }

            if (httpContext.StreamingBodyWriter is not null)
            {
                await WriteStreamingResponseAsync(
                    stream,
                    httpContext.Response,
                    headOnly,
                    httpContext.Response.StatusCode,
                    httpContext.StreamingBodyWriter,
                    ct);
            }
            else if (!httpContext.Response.IsStarted || httpContext.Response.IsBuffered)
                await WriteResponseAsync(stream, httpContext.Response, headOnly, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HTTP/3] {ex.GetType().Name}: {ex.Message}");
            try { stream.Abort(QuicAbortDirection.Both, Http3InternalError); } catch { }
        }
        finally
        {
            HttpContextPool.Return(httpContext);
            await stream.DisposeAsync();
        }
#pragma warning restore CA1416
    }

    private static async Task ConsumeControlStreamAsync(QuicStream stream, CancellationToken ct)
    {
        bool sawSettings = false;
        while (true)
        {
            var frame = await TryReadFrameAsync(stream, ct);
            if (frame is null)
                break;

            if (frame.Value.Type == FrameSettings)
            {
                if (sawSettings)
                    throw new InvalidOperationException("HTTP/3 control stream sent duplicate SETTINGS.");
                sawSettings = true;
            }
        }
    }

    private static async Task DrainStreamAsync(Stream stream, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            while (await stream.ReadAsync(buffer.AsMemory(), ct) > 0) { }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<ParsedHttp3RequestHead> ReadRequestHeadAsync(QuicStream stream, CancellationToken ct)
    {
#pragma warning disable CA1416
        while (true)
        {
            var frame = await TryReadFrameAsync(stream, ct);
            if (frame is null)
                throw new InvalidOperationException("HTTP/3 request missing HEADERS frame.");

            switch (frame.Value.Type)
            {
                case FrameHeaders:
                    return ParseRequestHead(DecodeFieldSection(frame.Value.Payload));
                case FrameData:
                    throw new InvalidOperationException("HTTP/3 DATA frame received before request HEADERS.");
            }
        }
#pragma warning restore CA1416
    }

    private static ParsedHttp3Request ParseRequest(byte[] requestBytes)
    {
        ReadOnlySpan<byte> data = requestBytes;
        int pos = 0;

        List<(string name, string value)> headers = [];
        using var body = new MemoryStream();

        while (pos < data.Length)
        {
            long frameType = ReadVarInt(data, ref pos);
            long frameLength = ReadVarInt(data, ref pos);
            if (frameLength < 0 || pos + frameLength > data.Length)
                throw new InvalidOperationException("Invalid HTTP/3 frame length.");

            var payload = data.Slice(pos, (int)frameLength);
            pos += (int)frameLength;

            switch (frameType)
            {
                case FrameHeaders when headers.Count == 0:
                    headers = DecodeFieldSection(payload);
                    break;
                case FrameData:
                    body.Write(payload);
                    break;
            }
        }

        if (headers.Count == 0)
            throw new InvalidOperationException("HTTP/3 request missing HEADERS frame.");

        var head = ParseRequestHead(headers);
        return new ParsedHttp3Request(
            head.Method,
            head.Path,
            head.QueryString,
            head.Host,
            head.Headers,
            body.ToArray());
    }

    private static ParsedHttp3RequestHead ParseRequestHead(IReadOnlyList<(string name, string value)> headers)
    {
        string method = "GET";
        string path = "/";
        string queryString = string.Empty;
        string? host = null;

        var headerDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool sawRegularHeader = false;
        foreach (var (name, value) in headers)
        {
            if (name.StartsWith(':'))
            {
                if (sawRegularHeader)
                    throw new InvalidOperationException("HTTP/3 pseudo headers must precede regular headers.");
            }
            else
            {
                sawRegularHeader = true;
            }

            switch (name)
            {
                case ":method":
                    method = value;
                    break;
                case ":path":
                    path = value;
                    break;
                case ":authority":
                    host = value;
                    headerDict["host"] = value;
                    break;
                case ":scheme":
                    break;
                default:
                    if (name.StartsWith(':'))
                        throw new InvalidOperationException($"Unsupported HTTP/3 pseudo header '{name}'.");
                    if (headerDict.TryGetValue(name, out var existing))
                        headerDict[name] = existing + ", " + value;
                    else
                        headerDict[name] = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(method))
            throw new InvalidOperationException("HTTP/3 request missing :method pseudo header.");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("HTTP/3 request missing :path pseudo header.");

        int queryIdx = path.IndexOf('?');
        if (queryIdx >= 0)
        {
            queryString = path[(queryIdx + 1)..];
            path = path[..queryIdx];
        }

        return new ParsedHttp3RequestHead(
            HttpMethodExtensions.Parse(method),
            path,
            queryString,
            host,
            headerDict);
    }

    internal static (string Method, string Path, string QueryString, string? Host, IReadOnlyDictionary<string, string> Headers, byte[] Body)
        ParseRequestForTests(byte[] requestBytes)
    {
        var parsed = ParseRequest(requestBytes);
        return (parsed.Method.ToString(), parsed.Path, parsed.QueryString, parsed.Host, parsed.Headers, parsed.Body);
    }

    private static List<(string name, string value)> DecodeFieldSection(ReadOnlySpan<byte> data)
    {
        int pos = 0;
        long requiredInsertCount = ReadPrefixedInteger(data, ref pos, 8);
        long deltaBase = ReadPrefixedInteger(data, ref pos, 7);
        bool signBit = (data[Math.Max(0, pos - 1)] & 0x80) != 0;

        if (requiredInsertCount != 0 || deltaBase != 0 || signBit)
            throw new NotSupportedException("Dynamic QPACK tables are not supported yet.");

        var headers = new List<(string name, string value)>(8);

        while (pos < data.Length)
        {
            byte b = data[pos];
            if ((b & 0x80) != 0)
            {
                bool isStatic = (b & 0x40) != 0;
                int index = (int)ReadPrefixedInteger(data, ref pos, 6);
                if (!isStatic) throw new NotSupportedException("Dynamic QPACK references are not supported yet.");
                headers.Add(GetStaticEntry(index));
            }
            else if ((b & 0x40) != 0)
            {
                bool isStatic = (b & 0x10) != 0;
                int nameIndex = (int)ReadPrefixedInteger(data, ref pos, 4);
                string name = isStatic
                    ? GetStaticEntry(nameIndex).name
                    : throw new NotSupportedException("Dynamic QPACK references are not supported yet.");
                string value = ReadStringLiteral(data, ref pos, 7, 0x80);
                headers.Add((name, value));
            }
            else if ((b & 0x20) != 0)
            {
                string name = ReadStringLiteral(data, ref pos, 3, 0x08);
                string value = ReadStringLiteral(data, ref pos, 7, 0x80);
                headers.Add((name, value));
            }
            else
            {
                throw new NotSupportedException("Unsupported QPACK field representation.");
            }
        }

        return headers;
    }

    internal static IReadOnlyList<(string name, string value)> DecodeFieldSectionForTests(byte[] data) =>
        DecodeFieldSection(data);

    private static (string name, string value) GetStaticEntry(int index)
    {
        if ((uint)index >= (uint)QpackStaticTable.Length)
            throw new InvalidOperationException($"Unsupported QPACK static index: {index}");
        return QpackStaticTable[index];
    }

    private static string ReadStringLiteral(ReadOnlySpan<byte> data, ref int pos, int prefixBits, byte huffmanMask)
    {
        bool huffman = (data[pos] & huffmanMask) != 0;
        int length = checked((int)ReadPrefixedInteger(data, ref pos, prefixBits));
        if (pos + length > data.Length)
            throw new InvalidOperationException("Invalid QPACK string length.");

        var bytes = data.Slice(pos, length);
        pos += length;
        return huffman
            ? HpackDecoder.DecodeHuffmanString(bytes)
            : Encoding.ASCII.GetString(bytes);
    }

    private static void PopulateHttpContext(
        HttpContext ctx,
        ParsedHttp3RequestHead request,
        Stream bodyStream,
        IServiceProvider services,
        string? remoteIp,
        CancellationToken ct)
    {
        ctx.Request.Method = request.Method;
        ctx.Request.Path = request.Path;
        ctx.Request.QueryString = request.QueryString;
        ctx.Request.Headers = request.Headers;
        ctx.Request.Query = ParseQuery(request.QueryString);
        ctx.Request.RouteValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ctx.Request.Body = [];
        ctx.Request.BodyStream = bodyStream;
        ctx.Request.BodyReader = null;
        ctx.Request.ContentLength =
            request.Headers.TryGetValue("content-length", out var contentLength) &&
            long.TryParse(contentLength, out var parsedLength)
                ? parsedLength
                : 0;
        ctx.Request.ContentType = request.Headers.TryGetValue("content-type", out var contentType) ? contentType : null;
        ctx.Request.Host = request.Host;
        ctx.Request.Authorization = request.Headers.TryGetValue("authorization", out var authorization) ? authorization : null;

        if (remoteIp is not null)
            ctx.Items["__RemoteIP"] = remoteIp;

        ctx.Initialize(services, ct);
        ctx._disposeScope = new Http11Connection.LazyScopeProvider(services);
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    private static async Task WriteResponseAsync(QuicStream stream, HttpResponse response, bool headOnly, CancellationToken ct)
    {
        var body = response.Body;
        if (!response.Headers.ContainsKey("Content-Length"))
            response.Headers["Content-Length"] = body.Length.ToString();

        var headerBlock = EncodeResponseHeaders(response);
        await WriteFrameAsync(stream, FrameHeaders, headerBlock, body.Length == 0 || headOnly, ct);

        if (body.Length > 0 && !headOnly)
            await WriteFrameAsync(stream, FrameData, body, true, ct);
    }

    private static async Task WriteStreamingResponseAsync(
        QuicStream stream,
        HttpResponse response,
        bool headOnly,
        int statusCode,
        Func<Stream, Task> bodyWriter,
        CancellationToken ct)
    {
        response.StatusCode = statusCode;
        response.Headers.Remove("Content-Length");
        if (!response.Headers.ContainsKey("Content-Type"))
            response.Headers["Content-Type"] = "application/x-ndjson";

        var headerBlock = EncodeResponseHeaders(response);
        await WriteFrameAsync(stream, FrameHeaders, headerBlock, false, ct);

        if (headOnly)
        {
#pragma warning disable CA1416
            stream.CompleteWrites();
#pragma warning restore CA1416
            return;
        }

        await using var bodyStream = new Http3DataFrameStream(stream);
        await bodyWriter(bodyStream);
        await bodyStream.CompleteAsync(ct);
    }

    private static byte[] EncodeResponseHeaders(HttpResponse response)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0); // Required Insert Count = 0
        ms.WriteByte(0); // Base = 0

        WriteLiteralHeader(ms, ":status", response.StatusCode.ToString());
        foreach (var header in response.Headers)
            WriteLiteralHeader(ms, header.Key.ToLowerInvariant(), header.Value);

        return ms.ToArray();
    }

    internal static byte[] EncodeResponseHeadersForTests(HttpResponse response) =>
        EncodeResponseHeaders(response);

    internal static byte[] EncodeFieldSectionForTests(params (string name, string value)[] headers)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0); // Required Insert Count = 0
        ms.WriteByte(0); // Base = 0

        foreach (var (name, value) in headers)
            WriteLiteralHeader(ms, name, value);

        return ms.ToArray();
    }

    internal static byte[] EncodeRequestForTests((string name, string value)[] headers, byte[]? body = null)
    {
        using var ms = new MemoryStream();
        var fieldSection = EncodeFieldSectionForTests(headers);
        WriteVarInt(ms, FrameHeaders);
        WriteVarInt(ms, fieldSection.LongLength);
        ms.Write(fieldSection, 0, fieldSection.Length);

        if (body is { Length: > 0 })
        {
            WriteVarInt(ms, FrameData);
            WriteVarInt(ms, body.LongLength);
            ms.Write(body, 0, body.Length);
        }

        return ms.ToArray();
    }

    private static void WriteLiteralHeader(Stream stream, string name, string value)
    {
        WritePrefixedInteger(stream, 0x20, 3, Encoding.ASCII.GetByteCount(name));
        var nameBytes = Encoding.ASCII.GetBytes(name);
        stream.Write(nameBytes, 0, nameBytes.Length);
        WritePrefixedInteger(stream, 0x00, 7, Encoding.UTF8.GetByteCount(value));
        var valueBytes = Encoding.UTF8.GetBytes(value);
        stream.Write(valueBytes, 0, valueBytes.Length);
    }

    private static async Task WriteFrameAsync(
        QuicStream stream,
        long frameType,
        byte[] payload,
        bool completeWrites,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WriteVarInt(ms, frameType);
        WriteVarInt(ms, payload.LongLength);
        ms.Write(payload, 0, payload.Length);
#pragma warning disable CA1416
        await stream.WriteAsync(ms.ToArray(), completeWrites, ct);
        if (!completeWrites)
            await stream.FlushAsync(ct);
        if (completeWrites)
            stream.CompleteWrites();
#pragma warning restore CA1416
    }

    private static async ValueTask<(long Type, byte[] Payload)?> TryReadFrameAsync(QuicStream stream, CancellationToken ct)
    {
        try
        {
            long frameType = await ReadVarIntAsync(stream, ct);
            long frameLength = await ReadVarIntAsync(stream, ct);
            if (frameLength < 0 || frameLength > int.MaxValue)
                throw new InvalidOperationException("Invalid HTTP/3 frame length.");

            var payload = await ReadExactlyAsync(stream, (int)frameLength, ct);
            return (frameType, payload);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    private static async Task WriteVarIntAsync(QuicStream stream, long value, CancellationToken ct)
    {
        Span<byte> buffer = stackalloc byte[8];
        int length = EncodeVarInt(value, buffer);
#pragma warning disable CA1416
        await stream.WriteAsync(buffer[..length].ToArray(), false, ct);
#pragma warning restore CA1416
    }

    private static async ValueTask<long> ReadVarIntAsync(Stream stream, CancellationToken ct)
    {
        var first = new byte[1];
        int read = await stream.ReadAsync(first, 0, 1, ct);
        if (read == 0) throw new EndOfStreamException();

        int length = 1 << (first[0] >> 6);
        var buffer = new byte[8];
        buffer[0] = first[0];
        int offset = 1;
        while (offset < length)
        {
            int chunk = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), ct);
            if (chunk == 0) throw new EndOfStreamException();
            offset += chunk;
        }
        int pos = 0;
        return ReadVarInt(buffer.AsSpan(0, length), ref pos);
    }

    private static long ReadVarInt(ReadOnlySpan<byte> data, ref int pos)
    {
        byte first = data[pos];
        int length = 1 << (first >> 6);
        long value = first & 0x3F;
        pos++;
        for (int i = 1; i < length; i++)
        {
            value = (value << 8) | data[pos++];
        }
        return value;
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken ct)
    {
        if (length == 0)
            return [];

        var buffer = GC.AllocateUninitializedArray<byte>(length);
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), ct);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }

        return buffer;
    }

    private static void WriteVarInt(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        int length = EncodeVarInt(value, buffer);
        stream.Write(buffer[..length]);
    }

    private static int EncodeVarInt(long value, Span<byte> destination)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value < 64)
        {
            destination[0] = (byte)value;
            return 1;
        }
        if (value < 16384)
        {
            destination[0] = (byte)(0x40 | ((value >> 8) & 0x3F));
            destination[1] = (byte)(value & 0xFF);
            return 2;
        }
        if (value < 1073741824)
        {
            destination[0] = (byte)(0x80 | ((value >> 24) & 0x3F));
            destination[1] = (byte)((value >> 16) & 0xFF);
            destination[2] = (byte)((value >> 8) & 0xFF);
            destination[3] = (byte)(value & 0xFF);
            return 4;
        }

        destination[0] = (byte)(0xC0 | ((value >> 56) & 0x3F));
        destination[1] = (byte)((value >> 48) & 0xFF);
        destination[2] = (byte)((value >> 40) & 0xFF);
        destination[3] = (byte)((value >> 32) & 0xFF);
        destination[4] = (byte)((value >> 24) & 0xFF);
        destination[5] = (byte)((value >> 16) & 0xFF);
        destination[6] = (byte)((value >> 8) & 0xFF);
        destination[7] = (byte)(value & 0xFF);
        return 8;
    }

    private static long ReadPrefixedInteger(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        int mask = (1 << prefixBits) - 1;
        long value = data[pos++] & mask;
        if (value < mask) return value;

        int shift = 0;
        while (true)
        {
            byte b = data[pos++];
            value += (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return value;
    }

    private static void WritePrefixedInteger(Stream stream, byte prefixPattern, int prefixBits, int value)
    {
        int mask = (1 << prefixBits) - 1;
        if (value < mask)
        {
            stream.WriteByte((byte)(prefixPattern | value));
            return;
        }

        stream.WriteByte((byte)(prefixPattern | mask));
        int remaining = value - mask;
        while (remaining >= 128)
        {
            stream.WriteByte((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }
        stream.WriteByte((byte)remaining);
    }

    private sealed record ParsedHttp3RequestHead(
        CosmoApiServer.Core.Http.HttpMethod Method,
        string Path,
        string QueryString,
        string? Host,
        IReadOnlyDictionary<string, string> Headers);

    private sealed record ParsedHttp3Request(
        CosmoApiServer.Core.Http.HttpMethod Method,
        string Path,
        string QueryString,
        string? Host,
        IReadOnlyDictionary<string, string> Headers,
        byte[] Body);

    private sealed class Http3RequestBodyStream(QuicStream stream) : Stream
    {
        private ReadOnlyMemory<byte> _currentData = ReadOnlyMemory<byte>.Empty;
        private bool _completed;
        private bool _disposed;

        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            await ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
#pragma warning disable CA1416
            if (_disposed || _completed || buffer.IsEmpty)
                return 0;

            while (_currentData.IsEmpty)
            {
                var frame = await TryReadFrameAsync(stream, cancellationToken);
                if (frame is null)
                {
                    _completed = true;
                    return 0;
                }

                switch (frame.Value.Type)
                {
                    case FrameData:
                        _currentData = frame.Value.Payload;
                        break;
                    case FrameHeaders:
                        throw new NotSupportedException("HTTP/3 trailers are not supported yet.");
                }
            }

            int toCopy = Math.Min(buffer.Length, _currentData.Length);
            _currentData[..toCopy].CopyTo(buffer);
            _currentData = _currentData[toCopy..];
            return toCopy;
#pragma warning restore CA1416
        }

        public async Task DrainAsync(CancellationToken ct)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                while (await ReadAsync(buffer.AsMemory(), ct) > 0) { }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class Http3DataFrameStream(QuicStream stream) : Stream, IAsyncDisposable
    {
        private readonly ArrayBufferWriter<byte> _staging = new(4096);
        private bool _disposed;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count > 0)
                _staging.Write(buffer.AsSpan(offset, count));
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!buffer.IsEmpty)
                _staging.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void WriteByte(byte value)
        {
            _staging.GetSpan(1)[0] = value;
            _staging.Advance(1);
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_staging.WrittenCount == 0)
                return;

            var payload = _staging.WrittenMemory.ToArray();
            _staging.Clear();
            await WriteFrameAsync(stream, FrameData, payload, false, cancellationToken);
        }

        public async Task CompleteAsync(CancellationToken ct)
        {
            if (_disposed)
                return;

            if (_staging.WrittenCount > 0)
            {
                var payload = _staging.WrittenMemory.ToArray();
                _staging.Clear();
                await WriteFrameAsync(stream, FrameData, payload, true, ct);
            }
            else
            {
#pragma warning disable CA1416
                stream.CompleteWrites();
#pragma warning restore CA1416
            }

            _disposed = true;
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
                await CompleteAsync(CancellationToken.None);
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
