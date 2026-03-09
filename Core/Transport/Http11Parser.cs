using System.Buffers;
using System.Text;
using Cosmo.Transport.Pipelines;

namespace CosmoApiServer.Core.Transport;

internal static class Http11Parser
{
    private static readonly byte Space = (byte)' ';
    private static readonly byte Colon = (byte)':';
    private static readonly byte[] CrLf = "\r\n"u8.ToArray();

    public static bool TryParse(ref ReadOnlySequence<byte> buffer, out ParsedRequest request)
    {
        request = default;
        var reader = new SequenceReader<byte>(buffer);

        // ── Request line (e.g. GET /path HTTP/1.1) ──────────────────────
        if (!reader.TryReadTo(out ReadOnlySequence<byte> methodSeq, Space)) return false;
        if (!reader.TryReadTo(out ReadOnlySequence<byte> targetSeq, Space)) return false;
        if (!reader.TryReadTo(out ReadOnlySequence<byte> _, CrLf)) return false;

        // ── Headers ─────────────────────────────────────────────────────
        var headers = new List<HeaderEntry>(16);
        long contentLength = 0;
        string? contentType = null;
        string? host = null;
        string? auth = null;
        bool chunkedTransfer = false;

        while (true)
        {
            if (reader.IsNext(CrLf, advancePast: true)) break; // End of headers

            if (!reader.TryReadTo(out ReadOnlySequence<byte> nameSeq, Colon)) return false;
            if (!reader.TryReadTo(out ReadOnlySequence<byte> valueSeq, CrLf)) return false;

            var entry = new HeaderEntry(nameSeq, valueSeq);
            headers.Add(entry);

            // Fast check for well-known headers
            if (entry.IsName("Content-Length"u8))
            {
                TryParseInt64(valueSeq, out contentLength);
            }
            else if (entry.IsName("Content-Type"u8))
            {
                contentType = entry.Value;
            }
            else if (entry.IsName("Host"u8))
            {
                host = entry.Value;
            }
            else if (entry.IsName("Authorization"u8))
            {
                auth = entry.Value;
            }
            else if (entry.IsName("Transfer-Encoding"u8) && entry.ValueContains("chunked"u8))
            {
                chunkedTransfer = true;
            }
        }

        // ── Body ─────────────────────────────────────────────────────────
        byte[] body;
        if (chunkedTransfer) {
            if (!TryReadChunkedBody(ref reader, out body)) return false;
        } else if (contentLength > 0) {
            if (reader.Remaining < contentLength) return false;
            body = new byte[contentLength];
            reader.TryCopyTo(body);
            reader.Advance(contentLength);
        } else {
            body = [];
        }

        buffer = buffer.Slice(reader.Position);
        
        // Materialize method and target as strings
        string method = Encoding.ASCII.GetString(methodSeq);
        string rawTarget = Encoding.UTF8.GetString(targetSeq);

        request = new ParsedRequest(method, rawTarget, headers, body, contentLength, contentType, host, auth);
        return true;
    }

    private static bool TryReadChunkedBody(ref SequenceReader<byte> reader, out byte[] body)
    {
        body = [];
        var chunks = new List<byte[]>();
        while (true) {
            if (!reader.TryReadTo(out ReadOnlySequence<byte> sizeSeq, CrLf)) return false;
            
            if (!TryParseHex(sizeSeq, out long chunkSize)) return false;

            if (chunkSize == 0) {
                reader.Advance(2); // skip trailing CRLF
                break;
            }

            if (reader.Remaining < chunkSize + 2) return false;
            byte[] chunk = new byte[chunkSize];
            reader.TryCopyTo(chunk);
            reader.Advance(chunkSize + 2); // data + CRLF
            chunks.Add(chunk);
        }
        body = new byte[chunks.Sum(c => c.Length)];
        int offset = 0;
        foreach (var c in chunks) { c.CopyTo(body, offset); offset += c.Length; }
        return true;
    }

    private static bool TryParseHex(ReadOnlySequence<byte> sequence, out long result)
    {
        result = 0;
        if (sequence.IsEmpty) return false;
        
        foreach (var memory in sequence)
        {
            foreach (byte b in memory.Span)
            {
                if (b == (byte)';') return true; // Start of extensions
                result <<= 4;
                if (b >= '0' && b <= '9') result += b - '0';
                else if (b >= 'a' && b <= 'f') result += b - 'a' + 10;
                else if (b >= 'A' && b <= 'F') result += b - 'A' + 10;
                else return false;
            }
        }
        return true;
    }

    private static bool TryParseInt64(ReadOnlySequence<byte> sequence, out long result)
    {
        result = 0;
        bool started = false;
        foreach (var memory in sequence)
        {
            foreach (byte b in memory.Span)
            {
                if (b >= '0' && b <= '9')
                {
                    result = result * 10 + (b - '0');
                    started = true;
                }
                else if (started && b == ' ') continue;
                else if (started) return true;
            }
        }
        return started;
    }

    public static bool TryDetectExpect100(in ReadOnlySequence<byte> buffer)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (!reader.TryReadTo(out ReadOnlySequence<byte> _, CrLf)) return false;
        
        while (true) {
            if (reader.IsNext(CrLf)) return false;
            if (!reader.TryReadTo(out ReadOnlySequence<byte> lineSeq, CrLf)) return false;
            
            var lineBytes = lineSeq.ToArray();
            var lineString = Encoding.ASCII.GetString(lineBytes);
            if (lineString.StartsWith("Expect:", StringComparison.OrdinalIgnoreCase) && 
                lineString.Contains("100-continue", StringComparison.OrdinalIgnoreCase)) return true;
        }
    }
}

public readonly struct HeaderEntry
{
    public string Name { get; }
    public string Value { get; }

    public HeaderEntry(ReadOnlySequence<byte> name, ReadOnlySequence<byte> value)
    {
        Name = Encoding.ASCII.GetString(name).Trim();
        Value = Encoding.UTF8.GetString(value).Trim();
    }

    public bool IsName(ReadOnlySpan<byte> nameUtf8)
    {
        // Compare using the materialized string but without new allocations
        if (Name.Length != nameUtf8.Length) return false;
        
        for (int i = 0; i < Name.Length; i++)
        {
            byte c1 = (byte)Name[i];
            byte c2 = nameUtf8[i];
            if (c1 == c2) continue;
            if (c1 >= 'A' && c1 <= 'Z') c1 |= 0x20;
            if (c2 >= 'A' && c2 <= 'Z') c2 |= 0x20;
            if (c1 != c2) return false;
        }
        return true;
    }

    public bool ValueContains(ReadOnlySpan<byte> valueUtf8)
    {
        return Value.Contains(Encoding.UTF8.GetString(valueUtf8), StringComparison.OrdinalIgnoreCase);
    }

    public void Deconstruct(out string name, out string value)
    {
        name = Name;
        value = Value;
    }
}

internal readonly struct ParsedRequest(
    string method, 
    string rawTarget, 
    List<HeaderEntry> headers, 
    byte[] body,
    long contentLength,
    string? contentType,
    string? host,
    string? auth)
{
    public readonly string Method = method;
    public readonly string RawTarget = rawTarget;
    public readonly List<HeaderEntry> Headers = headers;
    public readonly byte[] Body = body;
    public readonly long ContentLength = contentLength;
    public readonly string? ContentType = contentType;
    public readonly string? Host = host;
    public readonly string? Authorization = auth;
}
