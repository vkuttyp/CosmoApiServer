using System.Buffers;
using System.Text;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Parses HTTP/1.1 response status line and headers from a <see cref="ReadOnlySequence{T}"/>.
/// Client-side counterpart to <see cref="Http11Parser"/>.
/// </summary>
internal static class Http11ResponseParser
{
    private static readonly byte Space = (byte)' ';
    private static readonly byte[] CrLf = "\r\n"u8.ToArray();
    private static readonly byte Colon = (byte)':';

    /// <summary>
    /// Try to parse a complete HTTP/1.1 response (status line + headers) from the buffer.
    /// On success, <paramref name="buffer"/> is sliced to the start of the response body.
    /// </summary>
    public static bool TryParse(ref ReadOnlySequence<byte> buffer, out ParsedResponse response)
    {
        response = default;
        var reader = new SequenceReader<byte>(buffer);

        // Status line: HTTP/1.1 200 OK\r\n
        // Skip "HTTP/1.x "
        if (!reader.TryReadTo(out ReadOnlySequence<byte> _, Space)) return false;

        // Status code
        if (!reader.TryReadTo(out ReadOnlySequence<byte> statusSeq, Space)) return false;
        if (!TryParseInt(statusSeq, out int statusCode)) return false;

        // Reason phrase (consume until CRLF)
        if (!reader.TryReadTo(out ReadOnlySequence<byte> reasonSeq, CrLf)) return false;
        var reasonPhrase = Encoding.ASCII.GetString(reasonSeq).Trim();

        // Headers
        var headers = new List<HeaderEntry>(16);
        long contentLength = -1;
        bool chunked = false;
        bool connectionClose = false;

        while (true)
        {
            if (reader.IsNext(CrLf, advancePast: true)) break; // End of headers

            if (!reader.TryReadTo(out ReadOnlySequence<byte> nameSeq, Colon)) return false;
            if (!reader.TryReadTo(out ReadOnlySequence<byte> valueSeq, CrLf)) return false;

            var entry = new HeaderEntry(nameSeq, valueSeq);
            headers.Add(entry);

            if (entry.IsName("Content-Length"u8))
            {
                if (TryParseInt64(valueSeq, out var cl) && cl >= 0)
                    contentLength = cl;
            }
            else if (entry.IsName("Transfer-Encoding"u8) && entry.ValueContains("chunked"u8))
            {
                chunked = true;
            }
            else if (entry.IsName("Connection"u8) && entry.ValueContains("close"u8))
            {
                connectionClose = true;
            }
        }

        buffer = buffer.Slice(reader.Position);
        response = new ParsedResponse(statusCode, reasonPhrase, headers, contentLength, chunked, connectionClose);
        return true;
    }

    private static bool TryParseInt(ReadOnlySequence<byte> sequence, out int result)
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
                else if (b == ' ') continue;
                else if (started) return true;
                else return false;
            }
        }
        return started;
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
                else if (b == ' ') continue;
                else if (started) return true;
            }
        }
        return started;
    }
}

internal readonly struct ParsedResponse(
    int statusCode,
    string reasonPhrase,
    List<HeaderEntry> headers,
    long contentLength,
    bool chunked,
    bool connectionClose)
{
    public readonly int StatusCode = statusCode;
    public readonly string ReasonPhrase = reasonPhrase;
    public readonly List<HeaderEntry> Headers = headers;
    public readonly long ContentLength = contentLength;
    public readonly bool Chunked = chunked;
    public readonly bool ConnectionClose = connectionClose;
}
