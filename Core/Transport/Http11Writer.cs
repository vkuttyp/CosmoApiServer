using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Writes HTTP/1.1 responses directly into a <see cref="PipeWriter"/> — no intermediate byte[].
/// </summary>
internal static class Http11Writer
{
    private static readonly byte[] Http11Ok         = "HTTP/1.1 "u8.ToArray();
    private static readonly byte[] CrLf             = "\r\n"u8.ToArray();
    private static readonly byte[] HeaderSep        = ": "u8.ToArray();
    private static readonly byte[] ConnectionKA     = "Connection: keep-alive\r\n"u8.ToArray();
    private static readonly byte[] ContentTypeDef   = "Content-Type: text/plain\r\n"u8.ToArray();
    private static readonly byte[] TransferChunked  = "Transfer-Encoding: chunked\r\n"u8.ToArray();
    private static readonly byte[] ContentTypeNdjson= "Content-Type: application/x-ndjson\r\n"u8.ToArray();
    private static readonly byte[] ChunkTerminator  = "0\r\n\r\n"u8.ToArray();

    // Reason phrases for common status codes (avoids string lookup on hot path)
    private static ReadOnlySpan<byte> ReasonPhrase(int status) => status switch
    {
        200 => "200 OK"u8,
        201 => "201 Created"u8,
        204 => "204 No Content"u8,
        400 => "400 Bad Request"u8,
        401 => "401 Unauthorized"u8,
        403 => "403 Forbidden"u8,
        404 => "404 Not Found"u8,
        405 => "405 Method Not Allowed"u8,
        409 => "409 Conflict"u8,
        500 => "500 Internal Server Error"u8,
        _   => Encoding.ASCII.GetBytes($"{status} Unknown").AsSpan()
    };

    public static void WriteHeaders(PipeWriter writer, HttpResponse response, int? contentLength = null)
    {
        // Status line
        writer.Write(Http11Ok);
        writer.Write(ReasonPhrase(response.StatusCode));
        writer.Write(CrLf);

        // Standard headers
        writer.Write(ConnectionKA);

        bool hasContentType = response.Headers.ContainsKey("Content-Type");
        bool hasContentLength = response.Headers.ContainsKey("Content-Length");
        bool hasTransferEncoding = response.Headers.ContainsKey("Transfer-Encoding");

        // Custom headers (skip Content-Type and Content-Length; write after)
        foreach (var (name, value) in response.Headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            writer.Write(Encoding.ASCII.GetBytes(name));
            writer.Write(HeaderSep);
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write(CrLf);
        }

        // Content-Type
        if (hasContentType)
        {
            writer.Write("Content-Type: "u8);
            writer.Write(Encoding.UTF8.GetBytes(response.Headers["Content-Type"]));
            writer.Write(CrLf);
        }

        // Content-Length
        if (hasContentLength)
        {
            writer.Write("Content-Length: "u8);
            writer.Write(Encoding.ASCII.GetBytes(response.Headers["Content-Length"]));
            writer.Write(CrLf);
        }
        else if (contentLength.HasValue)
        {
            writer.Write("Content-Length: "u8);
            WriteInteger(writer, contentLength.Value);
            writer.Write(CrLf);
        }
        else if (!hasTransferEncoding)
        {
            // If no content length, use chunked encoding for HTTP/1.1
            writer.Write(TransferChunked);
        }

        // Blank line
        writer.Write(CrLf);
    }

    /// <summary>Write a complete buffered response to the pipe.</summary>
    public static void WriteResponse(PipeWriter writer, HttpResponse response)
    {
        WriteHeaders(writer, response, response.Body.Length);

        // Body
        if (response.Body.Length > 0)
            writer.Write(response.Body);
    }

    /// <summary>
    /// Write HTTP/1.1 response headers for a chunked streaming response,
    /// then return a <see cref="ChunkedBodyStream"/> the caller writes items into.
    /// </summary>
    public static async Task WriteStreamingResponseAsync(
        PipeWriter writer,
        int statusCode,
        Func<Stream, Task> bodyWriter,
        CancellationToken ct)
    {
        // Response headers
        writer.Write(Http11Ok);
        writer.Write(ReasonPhrase(statusCode));
        writer.Write(CrLf);
        writer.Write(TransferChunked);
        writer.Write(ContentTypeNdjson);
        writer.Write("Connection: close\r\n"u8);  // client knows end-of-body when connection closes
        writer.Write(CrLf);
        await writer.FlushAsync(ct);

        // Let the application write chunks
        var chunkStream = new ChunkedBodyStream(writer);
        try
        {
            await bodyWriter(chunkStream);
        }
        finally
        {
            // Terminating chunk
            writer.Write(ChunkTerminator);
            await writer.FlushAsync(ct);
        }
    }

    /// <summary>Write a non-negative integer as ASCII digits directly into the pipe.</summary>
    private static void WriteInteger(PipeWriter writer, int value)
    {
        Span<byte> buf = stackalloc byte[12];
        int pos = buf.Length;
        if (value == 0) { buf[--pos] = (byte)'0'; }
        else { while (value > 0) { buf[--pos] = (byte)('0' + value % 10); value /= 10; } }
        writer.Write(buf[pos..]);
    }

    /// <summary>
    /// A write-only <see cref="Stream"/> that wraps each write as an HTTP/1.1 chunked segment.
    /// </summary>
    internal sealed class ChunkedBodyStream(PipeWriter writer) : Stream
    {
        public override bool CanRead  => false;
        public override bool CanSeek  => false;
        public override bool CanWrite => true;
        public override long Length   => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int  Read(byte[] buffer, int offset, int count)  => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)         => throw new NotSupportedException();
        public override void SetLength(long value)                         => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).GetAwaiter().GetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
        {
            if (buffer.IsEmpty) return;
            // chunk-size in hex
            WriteHex(writer, buffer.Length);
            writer.Write("\r\n"u8);
            writer.Write(buffer.Span);
            writer.Write("\r\n"u8);
            await writer.FlushAsync(ct);
        }

        private static void WriteHex(PipeWriter w, int value)
        {
            Span<byte> hex = stackalloc byte[8];
            int pos = hex.Length;
            do
            {
                int nibble = value & 0xF;
                hex[--pos] = (byte)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
                value >>= 4;
            }
            while (value > 0);
            w.Write(hex[pos..]);
        }
    }
}
