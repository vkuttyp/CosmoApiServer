using System.Buffers;
using System.Text;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Parses HTTP/1.1 requests from a ReadOnlySequence&lt;byte&gt; supplied by a PipeReader.
/// All parsing is allocation-free for the common case (no query string, no route params).
/// Returns false (incomplete) when the buffer does not yet contain a full request.
/// </summary>
internal static class Http11Parser
{
    private static readonly byte[] CrLf        = "\r\n"u8.ToArray();
    private static readonly byte[] DoubleCrLf  = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] ChunkedEnd  = "0\r\n\r\n"u8.ToArray();

    /// <summary>
    /// Attempts to parse one complete HTTP/1.1 request from <paramref name="buffer"/>.
    /// On success, slices <paramref name="buffer"/> past the consumed bytes and populates
    /// <paramref name="request"/>. Returns false when more data is needed.
    /// </summary>
    public static bool TryParse(
        ref ReadOnlySequence<byte> buffer,
        out ParsedRequest request)
    {
        request = default;
        var reader = new SequenceReader<byte>(buffer);

        // ── Request line ─────────────────────────────────────────────────
        if (!reader.TryReadTo(out ReadOnlySequence<byte> requestLineSeq, (byte)'\n'))
            return false;

        var requestLine = requestLineSeq.IsSingleSegment
            ? requestLineSeq.FirstSpan
            : requestLineSeq.ToArray().AsSpan();

        // strip \r
        if (!requestLine.IsEmpty && requestLine[^1] == '\r')
            requestLine = requestLine[..^1];

        // parse METHOD SP /path SP HTTP/x.y
        int sp1 = requestLine.IndexOf((byte)' ');
        if (sp1 < 0) return false;
        int sp2 = requestLine[(sp1 + 1)..].IndexOf((byte)' ');
        if (sp2 < 0) return false;
        sp2 += sp1 + 1;

        var method    = Encoding.ASCII.GetString(requestLine[..sp1]);
        var rawTarget = Encoding.ASCII.GetString(requestLine[(sp1 + 1)..sp2]);
        // ignore version

        // ── Headers ─────────────────────────────────────────────────────
        var headers = new List<(string name, string value)>(8);
        long contentLength = 0;
        bool chunkedTransfer = false;

        while (true)
        {
            if (!reader.TryReadTo(out ReadOnlySequence<byte> lineSeq, (byte)'\n'))
                return false;

            var line = lineSeq.IsSingleSegment
                ? lineSeq.FirstSpan
                : lineSeq.ToArray().AsSpan();

            if (!line.IsEmpty && line[^1] == '\r') line = line[..^1];
            if (line.IsEmpty) break; // blank line = end of headers

            int colon = line.IndexOf((byte)':');
            if (colon < 0) continue; // malformed, skip

            var name  = Encoding.ASCII.GetString(line[..colon]).Trim();
            var value = Encoding.ASCII.GetString(line[(colon + 1)..]).Trim();
            headers.Add((name, value));

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                long.TryParse(value, out contentLength);
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                     value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                chunkedTransfer = true;
        }

        // ── Body ─────────────────────────────────────────────────────────
        byte[] body;
        if (chunkedTransfer)
        {
            if (!TryReadChunkedBody(ref reader, out body))
                return false;
        }
        else if (contentLength > 0)
        {
            if (reader.Remaining < contentLength)
                return false;

            body = new byte[contentLength];
            reader.TryCopyTo(body);
            reader.Advance(contentLength);
        }
        else
        {
            body = [];
        }

        buffer = buffer.Slice(reader.Position);
        request = new ParsedRequest(method, rawTarget, headers, body);
        return true;
    }

    /// <summary>Reads a chunked-encoded body and decodes it into a flat byte array.</summary>
    private static bool TryReadChunkedBody(ref SequenceReader<byte> reader, out byte[] body)
    {
        body = [];
        var chunks = new List<byte[]>();

        while (true)
        {
            // Read chunk-size line
            if (!reader.TryReadTo(out ReadOnlySequence<byte> sizeLineSeq, (byte)'\n'))
                return false;

            var sizeLine = sizeLineSeq.IsSingleSegment
                ? sizeLineSeq.FirstSpan
                : sizeLineSeq.ToArray().AsSpan();
            if (!sizeLine.IsEmpty && sizeLine[^1] == '\r') sizeLine = sizeLine[..^1];

            // Parse hex chunk size (ignore chunk extensions)
            int extIdx = sizeLine.IndexOf((byte)';');
            var hexPart = extIdx >= 0 ? sizeLine[..extIdx] : sizeLine;
            if (!TryParseHex(hexPart, out long chunkSize)) return false;

            if (chunkSize == 0)
            {
                // Trailing headers + final CRLF
                ReadOnlySequence<byte> _ignored;
                if (!reader.TryReadTo(out _ignored, (byte)'\n')) return false; // skip trailing CRLF
                break;
            }

            if (reader.Remaining < chunkSize + 2) return false; // +2 for trailing CRLF

            var chunk = new byte[chunkSize];
            reader.TryCopyTo(chunk);
            reader.Advance(chunkSize + 2); // skip data + CRLF
            chunks.Add(chunk);
        }

        // Flatten chunks
        int total = chunks.Sum(c => c.Length);
        body = new byte[total];
        int offset = 0;
        foreach (var c in chunks) { c.CopyTo(body, offset); offset += c.Length; }
        return true;
    }

    private static bool TryParseHex(ReadOnlySpan<byte> span, out long value)
    {
        value = 0;
        foreach (byte b in span)
        {
            int digit;
            if (b >= '0' && b <= '9')      digit = b - '0';
            else if (b >= 'a' && b <= 'f') digit = b - 'a' + 10;
            else if (b >= 'A' && b <= 'F') digit = b - 'A' + 10;
            else return false;
            value = (value << 4) | digit;
        }
        return true;
    }
}

/// <summary>Parsed HTTP/1.1 request — raw strings, not yet mapped to HttpRequest model.</summary>
internal readonly struct ParsedRequest
{
    public readonly string Method;
    public readonly string RawTarget;    // path + optional ?query
    public readonly List<(string name, string value)> Headers;
    public readonly byte[] Body;

    public ParsedRequest(
        string method,
        string rawTarget,
        List<(string name, string value)> headers,
        byte[] body)
    {
        Method    = method;
        RawTarget = rawTarget;
        Headers   = headers;
        Body      = body;
    }
}
