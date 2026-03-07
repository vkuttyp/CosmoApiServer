using System.Buffers;
using System.Text;
using Cosmo.Transport.Pipelines;

namespace CosmoApiServer.Core.Transport;

internal static class Http11Parser
{
    public static bool TryParse(ref ReadOnlySequence<byte> buffer, out ParsedRequest request)
    {
        request = default;
        var reader = new SequenceReader<byte>(buffer);

        // ── Request line ─────────────────────────────────────────────────
        string? requestLine = reader.ReadLine();
        if (requestLine == null) return false;

        int sp1 = requestLine.IndexOf(' ');
        if (sp1 < 0) return false;
        int sp2 = requestLine.IndexOf(' ', sp1 + 1);
        if (sp2 < 0) return false;

        var method    = requestLine[..sp1];
        var rawTarget = requestLine[(sp1 + 1)..sp2];

        // ── Headers ─────────────────────────────────────────────────────
        var headers = new List<(string name, string value)>(8);
        long contentLength = 0;
        bool chunkedTransfer = false;

        while (true)
        {
            string? line = reader.ReadLine();
            if (line == null) return false;
            if (line.Length == 0) break; // end of headers

            int colon = line.IndexOf(':');
            if (colon < 0) continue;

            var name  = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers.Add((name, value));

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) 
                long.TryParse(value, out contentLength);
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                chunkedTransfer = true;
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
        request = new ParsedRequest(method, rawTarget, headers, body);
        return true;
    }

    private static bool TryReadChunkedBody(ref SequenceReader<byte> reader, out byte[] body)
    {
        body = [];
        var chunks = new List<byte[]>();
        while (true) {
            string? sizeLine = reader.ReadLine();
            if (sizeLine == null) return false;
            
            int extIdx = sizeLine.IndexOf(';');
            string hexPart = extIdx >= 0 ? sizeLine[..extIdx] : sizeLine;
            if (!long.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out long chunkSize)) return false;

            if (chunkSize == 0) {
                reader.ReadLine(); // skip trailing CRLF
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

    public static bool TryDetectExpect100(in ReadOnlySequence<byte> buffer)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (reader.ReadLine() == null) return false;
        while (true) {
            string? line = reader.ReadLine();
            if (line == null) return false;
            if (line.Length == 0) return false;
            if (line.StartsWith("Expect:", StringComparison.OrdinalIgnoreCase) && line.Contains("100-continue", StringComparison.OrdinalIgnoreCase)) return true;
        }
    }
}

internal readonly struct ParsedRequest(string method, string rawTarget, List<(string name, string value)> headers, byte[] body)
{
    public readonly string Method = method;
    public readonly string RawTarget = rawTarget;
    public readonly List<(string name, string value)> Headers = headers;
    public readonly byte[] Body = body;
}
