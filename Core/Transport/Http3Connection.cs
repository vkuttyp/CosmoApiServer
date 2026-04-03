using System.Buffers;
using System.Net;
using System.Net.Quic;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Experimental HTTP/3 transport with a minimal request/response path for buffered handlers.
/// This currently supports basic request streams with a HEADERS frame followed by optional DATA/trailers.
/// Push and advanced HTTP/3 control handling are not implemented.
/// </summary>
internal static class Http3Connection
{
    private const long Http3GeneralProtocolError = 0x0101;
    private const long Http3InternalError = 0x0102;

    private const long FrameData = 0x00;
    private const long FrameHeaders = 0x01;
    private const long FrameSettings = 0x04;
    private const long FrameGoAway = 0x07;
    private const long MaxGoAwayRequestStreamId = 4611686018427387900L;
    private const int BufferedDataFrameChunkSize = 32 * 1024;
    private const int MaxRequestsPerConnection = 100;
    private static readonly bool TraceHttp3 = string.Equals(
        Environment.GetEnvironmentVariable("COSMO_HTTP3_TRACE"),
        "1",
        StringComparison.Ordinal);

    private const long StreamTypeControl = 0x00;
    private const long StreamTypeQpackEncoder = 0x02;
    private const long StreamTypeQpackDecoder = 0x03;

    private const long SettingsQpackMaxTableCapacity = 0x01;
    private const long SettingsQpackBlockedStreams = 0x07;

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
        var qpackState = new QpackDecoderState();
        var encoderState = new QpackEncoderState();
        var connectionState = new Http3ConnectionState();
        var controlStreams = await InitializeServerStreamsAsync(connection, ct);
        var streamTasks = new List<Task>(16);
        bool goAwaySent = false;
        qpackState.SetInsertCountIncrementSink(increment =>
        {
            _ = controlStreams.DecoderWriter.SendInsertCountIncrementAsync(increment, CancellationToken.None);
        });
        qpackState.SetEncoderCapacitySink(capacity => encoderState.SetCapacity(capacity));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var stream = await connection.AcceptInboundStreamAsync(ct);
                if (stream.Type == QuicStreamType.Bidirectional)
                    connectionState.ObserveRequestStream(stream.Id);
                Trace($"accept stream={stream.Id} type={stream.Type} reqCount={connectionState.RequestCount}");
                var streamTask = stream.Type == QuicStreamType.Bidirectional
                    ? HandleRequestStreamAsync(stream, pipeline, services, qpackState, controlStreams.DecoderWriter, encoderState, controlStreams.EncoderWriter, remoteIp, ct)
                    : HandleUnidirectionalStreamAsync(stream, qpackState, connectionState, ct);
                lock (streamTasks)
                {
                    streamTasks.Add(streamTask);
                    streamTasks.RemoveAll(static t => t.IsCompleted);
                }

                if (connectionState.RequestCount >= MaxRequestsPerConnection)
                {
                    if (!goAwaySent)
                    {
                        try
                        {
                            Trace($"goaway-send id={connectionState.GoAwayId} reqCount={connectionState.RequestCount}");
                            await controlStreams.ControlWriter.SendGoAwayAsync(connectionState.GoAwayId, CancellationToken.None);
                            goAwaySent = true;
                        }
                        catch { }
                    }
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (QuicException) { }
        finally
        {
            try
            {
                if (!goAwaySent)
                {
                    Trace($"goaway-final id={connectionState.GoAwayId} reqCount={connectionState.RequestCount}");
                    await controlStreams.ControlWriter.SendGoAwayAsync(connectionState.GoAwayId, CancellationToken.None);
                }
            }
            catch { }

            Task[] pending;
            lock (streamTasks)
                pending = streamTasks.Where(static t => !t.IsCompleted).ToArray();
            try
            {
                await Task.WhenAll(pending);
            }
            catch { }
        }
#pragma warning restore CA1416
    }

    private static async Task<ServerControlStreams> InitializeServerStreamsAsync(QuicConnection connection, CancellationToken ct)
    {
#pragma warning disable CA1416
        var control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, ct);
        var encoder = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, ct);
        var decoder = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, ct);

        await WriteVarIntAsync(control, StreamTypeControl, ct);

        var settingsPayload = new byte[16];
        int settingsLen = 0;
        settingsLen += EncodeVarInt(SettingsQpackMaxTableCapacity, settingsPayload.AsSpan(settingsLen));
        settingsLen += EncodeVarInt(0, settingsPayload.AsSpan(settingsLen));
        settingsLen += EncodeVarInt(SettingsQpackBlockedStreams, settingsPayload.AsSpan(settingsLen));
        settingsLen += EncodeVarInt(0, settingsPayload.AsSpan(settingsLen));
        await WriteFrameAsync(control, FrameSettings, settingsPayload.AsMemory(0, settingsLen), false, ct);

        await WriteVarIntAsync(encoder, StreamTypeQpackEncoder, ct);
        await encoder.FlushAsync(ct);

        await WriteVarIntAsync(decoder, StreamTypeQpackDecoder, ct);
        await decoder.FlushAsync(ct);
        return new ServerControlStreams(new ControlStreamWriter(control), new DecoderInstructionWriter(decoder), new EncoderInstructionWriter(encoder));
#pragma warning restore CA1416
    }

    private static async Task HandleUnidirectionalStreamAsync(QuicStream stream, QpackDecoderState qpackState, Http3ConnectionState connectionState, CancellationToken ct)
    {
#pragma warning disable CA1416
        try
        {
            long streamType = await ReadVarIntAsync(stream, ct);
            connectionState.RegisterPeerUnidirectionalStream(streamType);
            switch (streamType)
            {
                case StreamTypeControl:
                    await ConsumeControlStreamAsync(stream, qpackState, ct);
                    break;
                case StreamTypeQpackEncoder:
                    await ConsumeQpackEncoderStreamAsync(stream, qpackState, ct);
                    break;
                case StreamTypeQpackDecoder:
                    await DrainStreamAsync(stream, ct);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported HTTP/3 unidirectional stream type: {streamType}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            try { stream.Abort(QuicAbortDirection.Both, IsProtocolError(ex) ? Http3GeneralProtocolError : Http3InternalError); } catch { }
        }
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
        QpackDecoderState qpackState,
        DecoderInstructionWriter decoderWriter,
        QpackEncoderState encoderState,
        EncoderInstructionWriter encoderWriter,
        string? remoteIp,
        CancellationToken ct)
    {
#pragma warning disable CA1416
        var httpContext = HttpContextPool.Rent();
        var headerWriter = new ArrayBufferWriter<byte>(256);
        var fieldWriter = new ArrayBufferWriter<byte>(256);
        var encoderInstructionsWriter = new ArrayBufferWriter<byte>(64);
        bool abortStream = false;
        try
        {
            var requestHead = await ReadRequestHeadAsync(stream, qpackState, decoderWriter, ct);
            Trace($"stream={stream.Id} head method={requestHead.Method} path={requestHead.Path}");
            if (requestHead.RequiredInsertCount > 0)
                await decoderWriter.SendSectionAcknowledgmentAsync(stream.Id, ct);
            PopulateHttpContext(httpContext, requestHead, new Http3RequestBodyStream(
                stream,
                qpackState,
                decoderWriter,
                stream.Id,
                httpContext.Request), services, remoteIp, ct);
            bool headOnly = requestHead.Method == CosmoApiServer.Core.Http.HttpMethod.HEAD;
            httpContext.Response.StreamingResponseWriter =
                (statusCode, bodyWriter, writeCt) => WriteStreamingResponseAsync(stream, httpContext.Response, headOnly, statusCode, bodyWriter, writeCt, headerWriter, fieldWriter, encoderInstructionsWriter, encoderState, encoderWriter);

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
                    ct,
                    headerWriter,
                    fieldWriter,
                    encoderInstructionsWriter,
                    encoderState,
                    encoderWriter);
            }
            else if (!httpContext.Response.IsStarted || httpContext.Response.IsBuffered)
                await WriteResponseAsync(stream, httpContext.Response, headOnly, ct, stream.Id, headerWriter, fieldWriter, encoderInstructionsWriter, encoderState, encoderWriter);

            Trace($"stream={stream.Id} response-written status={httpContext.Response.StatusCode} body={httpContext.Response.Body.Length}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HTTP/3] stream={stream.Id} {ex.GetType().Name}: {ex.Message}");
            abortStream = true;
            try { stream.Abort(QuicAbortDirection.Both, IsProtocolError(ex) ? Http3GeneralProtocolError : Http3InternalError); } catch { }
        }
        finally
        {
            HttpContextPool.Return(httpContext);
            if (abortStream)
            {
                Trace($"stream={stream.Id} dispose-abort");
                await stream.DisposeAsync();
            }
            else
            {
                Trace($"stream={stream.Id} dispose-success");
                _ = DisposeSuccessfulRequestStreamAsync(stream);
            }
        }
#pragma warning restore CA1416
    }

    private static async Task DisposeSuccessfulRequestStreamAsync(QuicStream stream)
    {
#pragma warning disable CA1416
        try
        {
            await stream.DisposeAsync();
        }
        catch { }
#pragma warning restore CA1416
    }

    private static void Trace(string message)
    {
        if (TraceHttp3)
            Console.Error.WriteLine($"[HTTP/3 TRACE] {message}");
    }

    private static void TraceWrite(long? streamId, string message)
    {
        if (TraceHttp3 && streamId is not null)
            Console.Error.WriteLine($"[HTTP/3 TRACE] stream={streamId} write {message}");
    }

    private static async Task ConsumeControlStreamAsync(QuicStream stream, QpackDecoderState qpackState, CancellationToken ct)
    {
        bool sawSettings = false;
        while (true)
        {
            var frame = await TryReadFrameAsync(stream, ct);
            if (frame is null)
                break;

            ValidateControlFrameType(frame.Value.Type, sawSettings);

            if (frame.Value.Type == FrameSettings)
            {
                sawSettings = true;
                qpackState.ApplyPeerSettings(frame.Value.Payload);
            }
        }

        if (!sawSettings)
            throw new InvalidOperationException("HTTP/3 control stream must begin with SETTINGS.");
    }

    private static async Task ConsumeQpackEncoderStreamAsync(QuicStream stream, QpackDecoderState qpackState, CancellationToken ct)
    {
        var readBuffer = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(readBuffer.AsMemory(), ct)) > 0)
                qpackState.AppendEncoderStreamData(readBuffer.AsSpan(0, read));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
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

    private static async Task<ParsedHttp3RequestHead> ReadRequestHeadAsync(
        QuicStream stream,
        QpackDecoderState qpackState,
        DecoderInstructionWriter decoderWriter,
        CancellationToken ct)
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
                    var decoded = await DecodeFieldSectionAsync(frame.Value.Payload, qpackState, decoderWriter, stream.Id, ct);
                    try
                    {
                        return ParseRequestHead(decoded);
                    }
                    catch
                    {
                        if (decoded.RequiredInsertCount > 0)
                        {
                            try
                            {
                                await decoderWriter.SendStreamCancellationAsync(stream.Id, CancellationToken.None);
                            }
                            catch { }
                        }
                        throw;
                    }
                case FrameData:
                    throw new InvalidOperationException("HTTP/3 DATA frame received before request HEADERS.");
                default:
                    throw new InvalidOperationException($"HTTP/3 frame type {frame.Value.Type} is not valid before request HEADERS.");
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
                    headers = DecodeFieldSection(payload, null);
                    break;
                case FrameData:
                    body.Write(payload);
                    break;
                default:
                    throw new InvalidOperationException($"HTTP/3 request contained unsupported frame type {frameType}.");
            }
        }

        if (headers.Count == 0)
            throw new InvalidOperationException("HTTP/3 request missing HEADERS frame.");

        var head = ParseRequestHead(new DecodedFieldSection(headers, 0));
        return new ParsedHttp3Request(
            head.Method,
            head.Path,
            head.QueryString,
            head.Host,
            head.Headers,
            body.ToArray());
    }

    private static ParsedHttp3RequestHead ParseRequestHead(DecodedFieldSection fieldSection)
    {
        var headers = fieldSection.Headers;
        string method = "GET";
        string path = "/";
        string queryString = string.Empty;
        string? host = null;
        bool sawMethod = false;
        bool sawPath = false;
        bool sawScheme = false;
        bool sawAuthority = false;

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
                    if (sawMethod)
                        throw new InvalidOperationException("HTTP/3 request sent duplicate :method pseudo header.");
                    sawMethod = true;
                    method = value;
                    break;
                case ":path":
                    if (sawPath)
                        throw new InvalidOperationException("HTTP/3 request sent duplicate :path pseudo header.");
                    sawPath = true;
                    path = value;
                    break;
                case ":authority":
                    if (sawAuthority)
                        throw new InvalidOperationException("HTTP/3 request sent duplicate :authority pseudo header.");
                    sawAuthority = true;
                    host = value;
                    headerDict["host"] = value;
                    break;
                case ":scheme":
                    if (sawScheme)
                        throw new InvalidOperationException("HTTP/3 request sent duplicate :scheme pseudo header.");
                    sawScheme = true;
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
            headerDict,
            fieldSection.RequiredInsertCount);
    }

    internal static (string Method, string Path, string QueryString, string? Host, IReadOnlyDictionary<string, string> Headers, byte[] Body)
        ParseRequestForTests(byte[] requestBytes)
    {
        var parsed = ParseRequest(requestBytes);
        return (parsed.Method.ToString(), parsed.Path, parsed.QueryString, parsed.Host, parsed.Headers, parsed.Body);
    }

    private static List<(string name, string value)> DecodeFieldSection(ReadOnlySpan<byte> data, QpackDecoderState? qpackState)
    {
        int pos = 0;
        long encodedRequiredInsertCount = ReadPrefixedInteger(data, ref pos, 8);
        byte deltaBaseByte = data[pos];
        long deltaBase = ReadPrefixedInteger(data, ref pos, 7);
        bool signBit = (deltaBaseByte & 0x80) != 0;

        if ((encodedRequiredInsertCount != 0 || deltaBase != 0 || signBit) && qpackState is null)
            throw new NotSupportedException("Dynamic QPACK field sections require connection-level decoder state.");

        long requiredInsertCount = DecodeRequiredInsertCount(encodedRequiredInsertCount, qpackState);
        long @base = DecodeBase(requiredInsertCount, signBit, deltaBase);

        if (requiredInsertCount > 0 && qpackState is not null && requiredInsertCount > qpackState.InsertCount)
            throw new InvalidOperationException("HTTP/3 field section references dynamic table entries that are not available yet.");

        var headers = new List<(string name, string value)>(8);

        while (pos < data.Length)
        {
            byte b = data[pos];
            if ((b & 0x80) != 0)
            {
                bool isStatic = (b & 0x40) != 0;
                int index = (int)ReadPrefixedInteger(data, ref pos, 6);
                if (!isStatic)
                {
                    if (qpackState is null)
                        throw new NotSupportedException("Dynamic QPACK field references require connection-level decoder state.");
                    headers.Add(ResolveDynamicRelativeField(qpackState, @base, index));
                }
                else
                {
                    headers.Add(QpackDecoderState.GetStaticEntry(index));
                }
            }
            else if ((b & 0xF0) == 0x10)
            {
                if (qpackState is null)
                    throw new NotSupportedException("Dynamic QPACK field references require connection-level decoder state.");

                int index = (int)ReadPrefixedInteger(data, ref pos, 4);
                headers.Add(ResolveDynamicPostBaseField(qpackState, @base, index));
            }
            else if ((b & 0x40) != 0)
            {
                bool isStatic = (b & 0x10) != 0;
                int nameIndex = (int)ReadPrefixedInteger(data, ref pos, 4);
                string name = isStatic
                    ? QpackDecoderState.GetStaticEntry(nameIndex).name
                    : qpackState is not null
                        ? ResolveDynamicRelativeField(qpackState, @base, nameIndex).name
                        : throw new NotSupportedException("Dynamic QPACK field references require connection-level decoder state.");
                string value = ReadStringLiteral(data, ref pos, 7, 0x80);
                headers.Add((name, value));
            }
            else if ((b & 0xF0) == 0x00)
            {
                if (qpackState is null)
                    throw new NotSupportedException("Dynamic QPACK field references require connection-level decoder state.");

                int nameIndex = (int)ReadPrefixedInteger(data, ref pos, 3);
                string name = ResolveDynamicPostBaseField(qpackState, @base, nameIndex).name;
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
        DecodeFieldSection(data, null);

    internal static IReadOnlyList<(string name, string value)> DecodeFieldSectionForTests(byte[] data, QpackDecoderState state) =>
        DecodeFieldSection(data, state);

    internal static void ValidateControlFrameSequenceForTests(params long[] frameTypes)
    {
        bool sawSettings = false;
        foreach (var frameType in frameTypes)
        {
            ValidateControlFrameType(frameType, sawSettings);
            if (frameType == FrameSettings)
                sawSettings = true;
        }

        if (!sawSettings)
            throw new InvalidOperationException("HTTP/3 control stream must begin with SETTINGS.");
    }

    internal static void ValidateUnidirectionalStreamSequenceForTests(params long[] streamTypes)
    {
        var state = new Http3ConnectionState();
        foreach (var streamType in streamTypes)
            state.RegisterPeerUnidirectionalStream(streamType);
    }

    internal static long DetermineGoAwayIdForTests(params long[] requestStreamIds)
    {
        var state = new Http3ConnectionState();
        foreach (var streamId in requestStreamIds)
            state.ObserveRequestStream(streamId);
        return state.GoAwayId;
    }

    private static async Task<DecodedFieldSection> DecodeFieldSectionAsync(
        ReadOnlyMemory<byte> data,
        QpackDecoderState qpackState,
        DecoderInstructionWriter decoderWriter,
        long streamId,
        CancellationToken ct)
    {
        long requiredInsertCount = PeekRequiredInsertCount(data.Span, qpackState);
        if (requiredInsertCount > 0 && requiredInsertCount > qpackState.InsertCount)
        {
            try
            {
                await qpackState.WaitForInsertCountAsync(requiredInsertCount, ct);
            }
            catch
            {
                try
                {
                    await decoderWriter.SendStreamCancellationAsync(streamId, CancellationToken.None);
                }
                catch { }
                throw;
            }
        }

        try
        {
            return new DecodedFieldSection(DecodeFieldSection(data.Span, qpackState), requiredInsertCount);
        }
        catch
        {
            if (requiredInsertCount > 0)
            {
                try
                {
                    await decoderWriter.SendStreamCancellationAsync(streamId, CancellationToken.None);
                }
                catch { }
            }
            throw;
        }
    }

    private static (string name, string value) ResolveDynamicRelativeField(QpackDecoderState state, long @base, int relativeIndex)
    {
        long absoluteIndex = @base - relativeIndex - 1;
        if (absoluteIndex < 0)
            throw new InvalidOperationException("Invalid QPACK relative index.");
        return state.GetDynamicEntryByAbsoluteIndex(absoluteIndex);
    }

    private static (string name, string value) ResolveDynamicPostBaseField(QpackDecoderState state, long @base, int postBaseIndex)
    {
        long absoluteIndex = @base + postBaseIndex;
        return state.GetDynamicEntryByAbsoluteIndex(absoluteIndex);
    }

    private static long DecodeBase(long requiredInsertCount, bool signBit, long deltaBase)
    {
        if (!signBit)
            return requiredInsertCount + deltaBase;

        if (requiredInsertCount <= deltaBase)
            throw new InvalidOperationException("Invalid HTTP/3 Base value.");

        return requiredInsertCount - deltaBase - 1;
    }

    private static long DecodeRequiredInsertCount(long encodedRequiredInsertCount, QpackDecoderState? qpackState)
    {
        if (encodedRequiredInsertCount == 0)
            return 0;

        if (qpackState is null)
            throw new NotSupportedException("Dynamic QPACK field sections require connection-level decoder state.");

        int maxEntries = qpackState.MaxEntries;
        if (maxEntries <= 0)
            throw new InvalidOperationException("Dynamic QPACK references require a non-zero table capacity.");

        long fullRange = 2L * maxEntries;
        if (encodedRequiredInsertCount > fullRange)
            throw new InvalidOperationException("Invalid HTTP/3 Required Insert Count.");

        long maxValue = qpackState.InsertCount + maxEntries;
        long maxWrapped = (maxValue / fullRange) * fullRange;
        long requiredInsertCount = maxWrapped + encodedRequiredInsertCount - 1;

        if (requiredInsertCount > maxValue)
            requiredInsertCount -= fullRange;
        if (requiredInsertCount <= qpackState.InsertCount - fullRange)
            requiredInsertCount += fullRange;
        if (requiredInsertCount <= 0)
            throw new InvalidOperationException("Invalid HTTP/3 Required Insert Count.");

        return requiredInsertCount;
    }

    private static long PeekRequiredInsertCount(ReadOnlySpan<byte> data, QpackDecoderState qpackState)
    {
        int pos = 0;
        long encodedRequiredInsertCount = ReadPrefixedInteger(data, ref pos, 8);
        return DecodeRequiredInsertCount(encodedRequiredInsertCount, qpackState);
    }

    private static void ValidateControlFrameType(long frameType, bool sawSettings)
    {
        if (!sawSettings && frameType != FrameSettings)
            throw new InvalidOperationException("HTTP/3 control stream must begin with SETTINGS.");

        if (frameType == FrameSettings && sawSettings)
            throw new InvalidOperationException("HTTP/3 control stream sent duplicate SETTINGS.");

        if (frameType == FrameData || frameType == FrameHeaders || frameType == FrameGoAway)
            throw new InvalidOperationException($"HTTP/3 frame type {frameType} is not valid on the control stream.");
    }

    private static bool IsProtocolError(Exception ex) =>
        ex is InvalidOperationException or NotSupportedException;

    private sealed class Http3ConnectionState
    {
        private readonly object _gate = new();
        private bool _peerControlStreamSeen;
        private bool _peerQpackEncoderStreamSeen;
        private bool _peerQpackDecoderStreamSeen;
        private long _highestRequestStreamId = -1;
        private int _requestCount;

        public long GoAwayId
        {
            get
            {
                lock (_gate)
                    return _highestRequestStreamId >= 0 ? _highestRequestStreamId : MaxGoAwayRequestStreamId;
            }
        }

        public int RequestCount
        {
            get
            {
                lock (_gate)
                    return _requestCount;
            }
        }

        public void RegisterPeerUnidirectionalStream(long streamType)
        {
            lock (_gate)
            {
                switch (streamType)
                {
                    case StreamTypeControl:
                        if (_peerControlStreamSeen)
                            throw new InvalidOperationException("HTTP/3 peer opened duplicate control streams.");
                        _peerControlStreamSeen = true;
                        break;
                    case StreamTypeQpackEncoder:
                        if (_peerQpackEncoderStreamSeen)
                            throw new InvalidOperationException("HTTP/3 peer opened duplicate QPACK encoder streams.");
                        _peerQpackEncoderStreamSeen = true;
                        break;
                    case StreamTypeQpackDecoder:
                        if (_peerQpackDecoderStreamSeen)
                            throw new InvalidOperationException("HTTP/3 peer opened duplicate QPACK decoder streams.");
                        _peerQpackDecoderStreamSeen = true;
                        break;
                }
            }
        }

        public void ObserveRequestStream(long streamId)
        {
            lock (_gate)
            {
                _requestCount++;
                if (streamId > _highestRequestStreamId)
                    _highestRequestStreamId = streamId;
            }
        }
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
        ctx.Request.Trailers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    private static async Task WriteResponseAsync(QuicStream stream, HttpResponse response, bool headOnly, CancellationToken ct, long? traceStreamId, ArrayBufferWriter<byte> headerWriter, ArrayBufferWriter<byte> fieldWriter, ArrayBufferWriter<byte> encoderInstructionsWriter, QpackEncoderState encoderState, EncoderInstructionWriter encoderWriter)
    {
        var body = response.Body;
        if (!response.Headers.ContainsKey("Content-Type") && body.Length > 0)
            response.Headers["Content-Type"] = "text/plain";
        if (!response.Headers.ContainsKey("Content-Length"))
            response.Headers["Content-Length"] = body.Length.ToString();

        bool hasTrailers = response.Trailers.Count > 0;
        bool completeAfterHeaders = (body.Length == 0 || headOnly) && !hasTrailers;
        await EncodeAndSendResponseHeadersAsync(stream, response, headerWriter, fieldWriter, encoderInstructionsWriter, encoderState, encoderWriter, completeAfterHeaders, traceStreamId, ct);

        if (body.Length > 0 && !headOnly)
            await WriteBufferedDataFramesAsync(stream, body, !hasTrailers, ct);

        if (hasTrailers)
        {
            var trailerBlock = EncodeTrailingHeaders(response.Trailers, headerWriter);
            TraceWrite(traceStreamId, $"trailers len={trailerBlock.Length}");
            await WriteFrameAsync(stream, FrameHeaders, trailerBlock, true, ct);
        }
    }

    private static async Task WriteBufferedDataFramesAsync(
        QuicStream stream,
        ReadOnlyMemory<byte> body,
        bool completeWrites,
        CancellationToken ct)
    {
        if (body.IsEmpty)
        {
            if (completeWrites)
            {
#pragma warning disable CA1416
                stream.CompleteWrites();
#pragma warning restore CA1416
            }
            return;
        }

        int offset = 0;
        while (offset < body.Length)
        {
            int length = Math.Min(BufferedDataFrameChunkSize, body.Length - offset);
            bool isFinalChunk = offset + length >= body.Length;
            TraceWrite(stream.Id, $"buffered-data offset={offset} len={length}");
            await WriteFrameAsync(stream, FrameData, body.Slice(offset, length), completeWrites && isFinalChunk, ct);
            offset += length;
        }
    }

    private static async Task WriteStreamingResponseAsync(
        QuicStream stream,
        HttpResponse response,
        bool headOnly,
        int statusCode,
        Func<Stream, Task> bodyWriter,
        CancellationToken ct,
        ArrayBufferWriter<byte> headerWriter,
        ArrayBufferWriter<byte> fieldWriter,
        ArrayBufferWriter<byte> encoderInstructionsWriter,
        QpackEncoderState encoderState,
        EncoderInstructionWriter encoderWriter)
    {
        response.StatusCode = statusCode;
        response.Headers.Remove("Content-Length");
        if (!response.Headers.ContainsKey("Content-Type"))
            response.Headers["Content-Type"] = "application/x-ndjson";

        await EncodeAndSendResponseHeadersAsync(stream, response, headerWriter, fieldWriter, encoderInstructionsWriter, encoderState, encoderWriter, false, null, ct);

        if (headOnly)
        {
            if (response.Trailers.Count > 0)
            {
                var trailerBlock = EncodeTrailingHeaders(response.Trailers, headerWriter);
                await WriteFrameAsync(stream, FrameHeaders, trailerBlock, true, ct);
            }
            else
            {
#pragma warning disable CA1416
                stream.CompleteWrites();
#pragma warning restore CA1416
            }
            return;
        }

        await using var bodyStream = new Http3DataFrameStream(stream);
        await bodyWriter(bodyStream);
        ReadOnlyMemory<byte>? trailers = response.Trailers.Count > 0
            ? EncodeTrailingHeaders(response.Trailers, headerWriter)
            : null;
        await bodyStream.CompleteAsync(trailers, ct);
    }

    private static async Task EncodeAndSendResponseHeadersAsync(
        QuicStream stream,
        HttpResponse response,
        ArrayBufferWriter<byte> headerWriter,
        ArrayBufferWriter<byte> fieldWriter,
        ArrayBufferWriter<byte> encoderInstructionsWriter,
        QpackEncoderState encoderState,
        EncoderInstructionWriter encoderWriter,
        bool completeWrites,
        long? traceStreamId,
        CancellationToken ct)
    {
        ReadOnlyMemory<byte> headerBlock;

        if (encoderState.MaxCapacity > 0)
        {
            encoderInstructionsWriter.Clear();
            headerBlock = EncodeResponseHeadersDynamic(response, headerWriter, fieldWriter, encoderState, encoderInstructionsWriter);
            if (encoderInstructionsWriter.WrittenCount > 0)
            {
#pragma warning disable CA1416
                await encoderWriter.WriteRawAsync(encoderInstructionsWriter.WrittenMemory, ct);
#pragma warning restore CA1416
            }
        }
        else
        {
            headerBlock = EncodeResponseHeaders(response, headerWriter);
        }

        TraceWrite(traceStreamId, $"headers len={headerBlock.Length} complete={completeWrites}");
        await WriteFrameAsync(stream, FrameHeaders, headerBlock, completeWrites, ct);
    }

    /// <summary>
    /// Encodes response headers using the dynamic table where possible.
    /// Appends any required encoder stream insertion instructions to <paramref name="encoderInstructions"/>.
    /// Sets Required Insert Count and Base in the field section prefix per RFC 9204.
    /// </summary>
    private static ReadOnlyMemory<byte> EncodeResponseHeadersDynamic(
        HttpResponse response,
        ArrayBufferWriter<byte> writer,
        ArrayBufferWriter<byte> fieldWriter,
        QpackEncoderState encoderState,
        ArrayBufferWriter<byte> encoderInstructions)
    {
        // Encode field lines into the reusable field buffer so we can compute RIC before writing the prefix.
        fieldWriter.Clear();
        long maxAbsoluteIndex = -1;

        var statusValue = response.StatusCode.ToString();
        if (QpackDecoderState.TryGetStaticIndex(":status", statusValue, out int staticIdx))
        {
            WriteIndexedStaticField(fieldWriter, staticIdx);
        }
        else
        {
            long absIdx = encoderState.TryGetEntry(":status", statusValue, out long existing)
                ? existing
                : InsertAndWriteInstruction(":status", statusValue, encoderState, encoderInstructions,
                    QpackDecoderState.TryGetStaticNameIndex(":status", out int sni) ? sni : -1);
            if (absIdx >= 0)
            {
                WriteDynamicIndexedField(fieldWriter, absIdx, encoderState.InsertCount);
                if (absIdx > maxAbsoluteIndex) maxAbsoluteIndex = absIdx;
            }
            else
            {
                WriteHeaderField(fieldWriter, ":status", statusValue);
            }
        }

        foreach (var header in response.Headers)
        {
            var name = header.Key.ToLowerInvariant();
            var value = header.Value;

            if (QpackDecoderState.TryGetStaticIndex(name, value, out staticIdx))
            {
                WriteIndexedStaticField(fieldWriter, staticIdx);
                continue;
            }

            long absIdx = encoderState.TryGetEntry(name, value, out long existingAbs)
                ? existingAbs
                : InsertAndWriteInstruction(name, value, encoderState, encoderInstructions,
                    QpackDecoderState.TryGetStaticNameIndex(name, out int sni2) ? sni2 : -1);

            if (absIdx >= 0)
            {
                WriteDynamicIndexedField(fieldWriter, absIdx, encoderState.InsertCount);
                if (absIdx > maxAbsoluteIndex) maxAbsoluteIndex = absIdx;
            }
            else
            {
                WriteHeaderField(fieldWriter, name, value);
            }
        }

        long requiredInsertCount = maxAbsoluteIndex >= 0 ? maxAbsoluteIndex + 1 : 0;
        long encodedRic = encoderState.EncodeRequiredInsertCount(requiredInsertCount);

        // Revert to static-only if encoded RIC overflows a single-byte prefix (rare; large tables).
        if (encodedRic >= 128)
            return EncodeResponseHeaders(response, writer);

        // Write prefix + field lines into the main writer as one contiguous block.
        writer.Clear();
        var prefixSpan = writer.GetSpan(2);
        prefixSpan[0] = (byte)encodedRic; // encoded Required Insert Count
        prefixSpan[1] = 0x00;             // S=0, DeltaBase=0
        writer.Advance(2);
        var fieldSpan = writer.GetSpan(fieldWriter.WrittenCount);
        fieldWriter.WrittenSpan.CopyTo(fieldSpan);
        writer.Advance(fieldWriter.WrittenCount);

        return writer.WrittenMemory;
    }

    /// <summary>
    /// Attempts to insert <paramref name="name"/>/<paramref name="value"/> into the dynamic table.
    /// If successful, writes the encoder instruction into <paramref name="encoderInstructions"/>.
    /// Returns the absolute index of the inserted entry, or -1 on failure.
    /// </summary>
    private static long InsertAndWriteInstruction(
        string name, string value,
        QpackEncoderState encoderState,
        ArrayBufferWriter<byte> encoderInstructions,
        int staticNameIndex)
    {
        long absIdx = encoderState.Insert(name, value);
        if (absIdx < 0)
            return -1;

        if (staticNameIndex >= 0)
        {
            // Insert With Name Reference (static): 0b11nnnnnn, N=6
            WritePrefixedInteger(encoderInstructions, 0xC0, 6, staticNameIndex);
        }
        else
        {
            // Insert With Literal Name: 0b01nnnnnn, N=5
            var nameByteCount = Encoding.ASCII.GetByteCount(name);
            WritePrefixedInteger(encoderInstructions, 0x40, 5, nameByteCount);
            Encoding.ASCII.GetBytes(name, encoderInstructions.GetSpan(nameByteCount));
            encoderInstructions.Advance(nameByteCount);
        }
        var valueByteCount = Encoding.UTF8.GetByteCount(value);
        WritePrefixedInteger(encoderInstructions, 0x00, 7, valueByteCount);
        Encoding.UTF8.GetBytes(value, encoderInstructions.GetSpan(valueByteCount));
        encoderInstructions.Advance(valueByteCount);

        return absIdx;
    }

    /// <summary>
    /// Writes an Indexed Field Line referencing the dynamic table (RFC 9204 §3.2.4).
    /// relativeIndex = (insertCount - 1 - absoluteIndex), pattern = 0b10xxxxxx, N=6.
    /// </summary>
    private static void WriteDynamicIndexedField(ArrayBufferWriter<byte> writer, long absoluteIndex, long insertCount)
    {
        long relativeIndex = insertCount - 1 - absoluteIndex;
        WritePrefixedInteger(writer, 0x80, 6, checked((int)relativeIndex));
    }

    private static ReadOnlyMemory<byte> EncodeResponseHeaders(HttpResponse response, ArrayBufferWriter<byte> writer)
    {
        writer.Clear();
        var prefix = writer.GetSpan(2);
        prefix[0] = 0; // Required Insert Count = 0
        prefix[1] = 0; // Base = 0
        writer.Advance(2);

        WriteHeaderField(writer, ":status", response.StatusCode.ToString());
        foreach (var header in response.Headers)
            WriteHeaderField(writer, header.Key.ToLowerInvariant(), header.Value);

        return writer.WrittenMemory;
    }

    internal static byte[] EncodeResponseHeadersForTests(HttpResponse response)
    {
        var writer = new ArrayBufferWriter<byte>(128);
        EncodeResponseHeaders(response, writer);
        return writer.WrittenSpan.ToArray();
    }

    internal static byte[] EncodeTrailingHeadersForTests(IReadOnlyDictionary<string, string> trailers)
    {
        var writer = new ArrayBufferWriter<byte>(64);
        EncodeTrailingHeaders(trailers, writer);
        return writer.WrittenSpan.ToArray();
    }

    internal static byte[] EncodeFieldSectionForTests(params (string name, string value)[] headers)
    {
        var writer = new ArrayBufferWriter<byte>(128);
        var prefix = writer.GetSpan(2);
        prefix[0] = 0; // Required Insert Count = 0
        prefix[1] = 0; // Base = 0
        writer.Advance(2);

        foreach (var (name, value) in headers)
            WriteHeaderField(writer, name, value);

        return writer.WrittenSpan.ToArray();
    }

    private static ReadOnlyMemory<byte> EncodeTrailingHeaders(IReadOnlyDictionary<string, string> trailers, ArrayBufferWriter<byte> writer)
    {
        writer.Clear();
        var prefix = writer.GetSpan(2);
        prefix[0] = 0; // Required Insert Count = 0
        prefix[1] = 0; // Base = 0
        writer.Advance(2);

        foreach (var trailer in trailers)
        {
            if (trailer.Key.StartsWith(':'))
                throw new InvalidOperationException("HTTP/3 trailers cannot contain pseudo headers.");
            WriteHeaderField(writer, trailer.Key.ToLowerInvariant(), trailer.Value);
        }

        return writer.WrittenMemory;
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

    private static void WriteLiteralHeader(ArrayBufferWriter<byte> writer, string name, string value)
    {
        var nameByteCount = Encoding.ASCII.GetByteCount(name);
        WritePrefixedInteger(writer, 0x20, 3, nameByteCount);
        Encoding.ASCII.GetBytes(name, writer.GetSpan(nameByteCount));
        writer.Advance(nameByteCount);
        var valueByteCount = Encoding.UTF8.GetByteCount(value);
        WritePrefixedInteger(writer, 0x00, 7, valueByteCount);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(valueByteCount));
        writer.Advance(valueByteCount);
    }

    private static void WriteHeaderField(ArrayBufferWriter<byte> writer, string name, string value)
    {
        if (QpackDecoderState.TryGetStaticIndex(name, value, out int staticIndex))
        {
            WriteIndexedStaticField(writer, staticIndex);
            return;
        }

        if (QpackDecoderState.TryGetStaticNameIndex(name, out int nameIndex))
        {
            WriteLiteralHeaderWithStaticNameReference(writer, nameIndex, value);
            return;
        }

        WriteLiteralHeader(writer, name, value);
    }

    private static void WriteIndexedStaticField(ArrayBufferWriter<byte> writer, int index) =>
        WritePrefixedInteger(writer, 0xC0, 6, index);

    private static void WriteLiteralHeaderWithStaticNameReference(ArrayBufferWriter<byte> writer, int nameIndex, string value)
    {
        WritePrefixedInteger(writer, 0x50, 4, nameIndex);
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WritePrefixedInteger(writer, 0x00, 7, byteCount);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(byteCount));
        writer.Advance(byteCount);
    }

    private static async Task WriteFrameAsync(
        QuicStream stream,
        long frameType,
        ReadOnlyMemory<byte> payload,
        bool completeWrites,
        CancellationToken ct)
    {
        var header = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            int headerLength = EncodeVarInt(frameType, header.AsSpan());
            headerLength += EncodeVarInt(payload.Length, header.AsSpan(headerLength));
#pragma warning disable CA1416
            bool completeOnHeader = completeWrites && payload.IsEmpty;
            await stream.WriteAsync(header.AsMemory(0, headerLength), completeOnHeader, ct);
            if (!payload.IsEmpty)
                await stream.WriteAsync(payload, completeWrites, ct);
            if (!completeWrites)
                await stream.FlushAsync(ct);
#pragma warning restore CA1416
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
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
        var buffer = ArrayPool<byte>.Shared.Rent(8);
        try
        {
            int length = EncodeVarInt(value, buffer);
#pragma warning disable CA1416
            await stream.WriteAsync(buffer.AsMemory(0, length), false, ct);
#pragma warning restore CA1416
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async ValueTask<long> ReadVarIntAsync(Stream stream, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8);
        try
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
            if (read == 0) throw new EndOfStreamException();

            int length = 1 << (buffer[0] >> 6);
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
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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

    private static void WritePrefixedInteger(ArrayBufferWriter<byte> writer, byte prefixPattern, int prefixBits, int value)
    {
        int mask = (1 << prefixBits) - 1;
        // max bytes: 1 prefix + up to 5 continuation bytes for a 32-bit value
        var span = writer.GetSpan(6);
        int pos = 0;
        if (value < mask)
        {
            span[pos++] = (byte)(prefixPattern | value);
        }
        else
        {
            span[pos++] = (byte)(prefixPattern | mask);
            int remaining = value - mask;
            while (remaining >= 128)
            {
                span[pos++] = (byte)((remaining & 0x7F) | 0x80);
                remaining >>= 7;
            }
            span[pos++] = (byte)remaining;
        }
        writer.Advance(pos);
    }

    private sealed record DecodedFieldSection(
        IReadOnlyList<(string name, string value)> Headers,
        long RequiredInsertCount);

    private sealed record ParsedHttp3RequestHead(
        CosmoApiServer.Core.Http.HttpMethod Method,
        string Path,
        string QueryString,
        string? Host,
        IReadOnlyDictionary<string, string> Headers,
        long RequiredInsertCount);

    private sealed record ParsedHttp3Request(
        CosmoApiServer.Core.Http.HttpMethod Method,
        string Path,
        string QueryString,
        string? Host,
        IReadOnlyDictionary<string, string> Headers,
        byte[] Body);

    private sealed class ControlStreamWriter(QuicStream stream)
    {
        private readonly SemaphoreSlim _lock = new(1, 1);

        public async Task SendGoAwayAsync(long requestStreamId, CancellationToken ct)
        {
            using var payload = new MemoryStream();
            WriteVarInt(payload, requestStreamId);
            using var frame = new MemoryStream();
            WriteVarInt(frame, FrameGoAway);
            WriteVarInt(frame, payload.Length);
            payload.Position = 0;
            payload.CopyTo(frame);

#pragma warning disable CA1416
            await _lock.WaitAsync(ct);
            try
            {
                await stream.WriteAsync(frame.ToArray(), false, ct);
                await stream.FlushAsync(ct);
            }
            finally
            {
                _lock.Release();
            }
#pragma warning restore CA1416
        }
    }

    private sealed record ServerControlStreams(ControlStreamWriter ControlWriter, DecoderInstructionWriter DecoderWriter, EncoderInstructionWriter EncoderWriter);

    private sealed class EncoderInstructionWriter(QuicStream stream)
    {
        private readonly SemaphoreSlim _lock = new(1, 1);

        // Insert With Literal Name (RFC 9204 §3.2.4): 0b01nnnnnn prefix, N=5
        public async Task WriteInsertWithLiteralNameAsync(string name, string value, CancellationToken ct)
        {
            var writer = new ArrayBufferWriter<byte>(64);
            var nameByteCount = Encoding.ASCII.GetByteCount(name);
            WritePrefixedInteger(writer, 0x40, 5, nameByteCount);
            Encoding.ASCII.GetBytes(name, writer.GetSpan(nameByteCount));
            writer.Advance(nameByteCount);
            var valueByteCount = Encoding.UTF8.GetByteCount(value);
            WritePrefixedInteger(writer, 0x00, 7, valueByteCount);
            Encoding.UTF8.GetBytes(value, writer.GetSpan(valueByteCount));
            writer.Advance(valueByteCount);
            await WriteRawAsync(writer.WrittenMemory, ct);
        }

        // Insert With Name Reference, static table (RFC 9204 §3.2.4): 0b11nnnnnn prefix, N=6
        public async Task WriteInsertWithStaticNameRefAsync(int staticNameIndex, string value, CancellationToken ct)
        {
            var writer = new ArrayBufferWriter<byte>(32);
            WritePrefixedInteger(writer, 0xC0, 6, staticNameIndex);
            var valueByteCount = Encoding.UTF8.GetByteCount(value);
            WritePrefixedInteger(writer, 0x00, 7, valueByteCount);
            Encoding.UTF8.GetBytes(value, writer.GetSpan(valueByteCount));
            writer.Advance(valueByteCount);
            await WriteRawAsync(writer.WrittenMemory, ct);
        }

        public async Task WriteRawAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
        {
#pragma warning disable CA1416
            await _lock.WaitAsync(ct);
            try
            {
                await stream.WriteAsync(payload, false, ct);
                await stream.FlushAsync(ct);
            }
            finally
            {
                _lock.Release();
            }
#pragma warning restore CA1416
        }
    }

    private sealed class DecoderInstructionWriter(QuicStream stream)
    {
        private readonly SemaphoreSlim _lock = new(1, 1);

        public async Task SendSectionAcknowledgmentAsync(long streamId, CancellationToken ct)
        {
            var writer = new ArrayBufferWriter<byte>(8);
            WritePrefixedInteger(writer, 0x80, 7, checked((int)streamId));
            await WriteInstructionAsync(writer.WrittenMemory, ct);
        }

        public async Task SendInsertCountIncrementAsync(int increment, CancellationToken ct)
        {
            if (increment <= 0)
                return;

            var writer = new ArrayBufferWriter<byte>(8);
            WritePrefixedInteger(writer, 0x00, 6, increment);
            await WriteInstructionAsync(writer.WrittenMemory, ct);
        }

        public async Task SendStreamCancellationAsync(long streamId, CancellationToken ct)
        {
            var writer = new ArrayBufferWriter<byte>(8);
            WritePrefixedInteger(writer, 0x40, 6, checked((int)streamId));
            await WriteInstructionAsync(writer.WrittenMemory, ct);
        }

        private async Task WriteInstructionAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
        {
#pragma warning disable CA1416
            await _lock.WaitAsync(ct);
            try
            {
                await stream.WriteAsync(payload, false, ct);
                await stream.FlushAsync(ct);
            }
            finally
            {
                _lock.Release();
            }
#pragma warning restore CA1416
        }
    }

    private sealed class Http3RequestBodyStream(
        QuicStream stream,
        QpackDecoderState qpackState,
        DecoderInstructionWriter decoderWriter,
        long streamId,
        HttpRequest request) : Stream
    {
        private ReadOnlyMemory<byte> _currentData = ReadOnlyMemory<byte>.Empty;
        private bool _endOfBody;
        private bool _trailersSeen;
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
            if (_disposed || _endOfBody || buffer.IsEmpty)
                return 0;

            while (_currentData.IsEmpty)
            {
                var frame = await TryReadFrameAsync(stream, cancellationToken);
                if (frame is null)
                {
                    _endOfBody = true;
                    return 0;
                }

                switch (frame.Value.Type)
                {
                    case FrameData:
                        if (_trailersSeen)
                            throw new InvalidOperationException("HTTP/3 DATA frame received after trailers.");
                        _currentData = frame.Value.Payload;
                        break;
                    case FrameHeaders:
                        if (_trailersSeen)
                            throw new InvalidOperationException("HTTP/3 request sent multiple trailer HEADERS frames.");
                        var trailers = await DecodeFieldSectionAsync(frame.Value.Payload, qpackState, decoderWriter, streamId, cancellationToken);
                        if (trailers.RequiredInsertCount > 0)
                            await decoderWriter.SendSectionAcknowledgmentAsync(streamId, cancellationToken);
                        MergeTrailers(request, trailers.Headers);
                        _trailersSeen = true;
                        continue;
                    default:
                        throw new InvalidOperationException($"HTTP/3 frame type {frame.Value.Type} is not valid in the request body stream.");
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

        // DATA frames ≤ this threshold are coalesced into a single WriteAsync to reduce async ops.
        private const int CoalesceThreshold = 8 * 1024;

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            int count = _staging.WrittenCount;
            if (count == 0)
                return;

            var payload = _staging.WrittenMemory;
            if (count <= CoalesceThreshold)
            {
                // Encode frame type (0x00) + length varint — at most 1+8 = 9 bytes.
                Span<byte> headerBuf = stackalloc byte[9];
                int headerLen = EncodeVarInt(FrameData, headerBuf);
                headerLen += EncodeVarInt(count, headerBuf[headerLen..]);

                var combined = ArrayPool<byte>.Shared.Rent(headerLen + count);
                try
                {
                    headerBuf[..headerLen].CopyTo(combined);
                    payload.Span.CopyTo(combined.AsSpan(headerLen));
#pragma warning disable CA1416
                    await stream.WriteAsync(combined.AsMemory(0, headerLen + count), false, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
#pragma warning restore CA1416
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(combined);
                }
            }
            else
            {
                await WriteFrameAsync(stream, FrameData, payload, false, cancellationToken);
            }

            _staging.Clear();
        }

        public async Task CompleteAsync(ReadOnlyMemory<byte>? trailerBlock, CancellationToken ct)
        {
            if (_disposed)
                return;

            if (_staging.WrittenCount > 0)
            {
                var payload = _staging.WrittenMemory;
                await WriteFrameAsync(stream, FrameData, payload, trailerBlock is null, ct);
                _staging.Clear();
            }

            if (trailerBlock is not null)
            {
                await WriteFrameAsync(stream, FrameHeaders, trailerBlock.Value, true, ct);
            }
            else if (_staging.WrittenCount == 0)
            {
                await WriteFrameAsync(stream, FrameData, ReadOnlyMemory<byte>.Empty, true, ct);
            }

            _disposed = true;
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
                await CompleteAsync(null, CancellationToken.None);
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }

    private static void MergeTrailers(HttpRequest request, IReadOnlyList<(string name, string value)> trailers)
    {
        Dictionary<string, string> trailerDict = request.Trailers as Dictionary<string, string>
            ?? new Dictionary<string, string>(request.Trailers, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in trailers)
        {
            if (name.StartsWith(':'))
                throw new InvalidOperationException("HTTP/3 trailers cannot contain pseudo headers.");

            if (trailerDict.TryGetValue(name, out var existing))
                trailerDict[name] = existing + ", " + value;
            else
                trailerDict[name] = value;
        }

        request.Trailers = trailerDict;
    }
}
