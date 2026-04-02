using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
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
    private static readonly byte[] ConnectionClose  = "Connection: close\r\n"u8.ToArray();
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
    /// then stream body bytes via a staging <see cref="ChunkedBodyStream"/>.
    /// Multiple small <see cref="Stream.WriteAsync"/> calls (e.g. from <see cref="System.Text.Json.JsonSerializer"/>)
    /// are coalesced into a single chunk per <see cref="Stream.FlushAsync"/> call,
    /// keeping the connection alive for subsequent requests.
    /// </summary>
    public static async Task WriteStreamingResponseAsync(
        PipeWriter writer,
        int statusCode,
        Func<Stream, Task> bodyWriter,
        CancellationToken ct)
    {
        // Response headers — chunked keep-alive to amortise TCP setup across requests.
        writer.Write(Http11Ok);
        writer.Write(ReasonPhrase(statusCode));
        writer.Write(CrLf);
        writer.Write(TransferChunked);
        writer.Write(ContentTypeNdjson);
        writer.Write(CrLf);

        // Stage all writes between FlushAsync calls into a single chunk each.
        var chunkStream = new ChunkedBodyStream(writer);
        try
        {
            await bodyWriter(chunkStream);
        }
        finally
        {
            // Drain any unflushed staged bytes before the terminating chunk.
            await chunkStream.FlushAsync(ct);
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
    /// A write-only <see cref="Stream"/> that batches all writes into a staging buffer and
    /// emits a single HTTP/1.1 chunk per <see cref="FlushAsync"/> call.
    /// This avoids one chunk-header per <see cref="WriteAsync"/> call, which is the pattern
    /// used by <see cref="System.Text.Json.JsonSerializer.SerializeAsync"/> internally.
    /// </summary>
    internal sealed class ChunkedBodyStream(PipeWriter writer) : Stream
    {
        private readonly ArrayBufferWriter<byte> _staging = new(4096);
        private ReadOnlyMemory<byte> _directSegment = ReadOnlyMemory<byte>.Empty;

        public override bool CanRead  => false;
        public override bool CanSeek  => false;
        public override bool CanWrite => true;
        public override long Length   => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int  Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)        => throw new NotSupportedException();
        public override void SetLength(long value)                        => throw new NotSupportedException();

        // Stage bytes — no chunk headers written yet
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return;

            if (offset == 0 && count == buffer.Length && _staging.WrittenCount == 0 && _directSegment.IsEmpty)
            {
                // Fast path for common NDJSON streaming: the caller already provides a stable byte[].
                _directSegment = buffer;
                return;
            }

            AppendToStaging(buffer.AsSpan(offset, count));
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
        {
            if (buffer.IsEmpty) return ValueTask.CompletedTask;

            if (_staging.WrittenCount == 0 && _directSegment.IsEmpty &&
                MemoryMarshal.TryGetArray(buffer, out var segment) &&
                segment.Array is not null &&
                segment.Offset == 0 &&
                segment.Count == segment.Array.Length)
            {
                _directSegment = buffer;
                return ValueTask.CompletedTask;
            }

            AppendToStaging(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void WriteByte(byte value)
        {
            EnsureDirectSegmentMovedToStaging();
            _staging.GetSpan(1)[0] = value;
            _staging.Advance(1);
        }

        // Sync Flush is a no-op — callers must use FlushAsync to emit a chunk.
        // Blocking on FlushAsync here would risk thread-pool starvation.
        public override void Flush() { }

        public override async Task FlushAsync(CancellationToken ct)
        {
            if (!_directSegment.IsEmpty)
            {
                WriteChunkFast(writer, _directSegment.Span);
                _directSegment = ReadOnlyMemory<byte>.Empty;
            }
            else if (_staging.WrittenCount > 0)
            {
                WriteChunkFast(writer, _staging.WrittenSpan);
                _staging.Clear();
            }
            await writer.FlushAsync(ct);
        }

        private void AppendToStaging(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return;
            EnsureDirectSegmentMovedToStaging();
            _staging.Write(data);
        }

        private void EnsureDirectSegmentMovedToStaging()
        {
            if (_directSegment.IsEmpty) return;
            _staging.Write(_directSegment.Span);
            _directSegment = ReadOnlyMemory<byte>.Empty;
        }

        private static void WriteChunkFast(PipeWriter writer, ReadOnlySpan<byte> payload)
        {
            if (payload.IsEmpty) return;

            Span<byte> hexBuffer = stackalloc byte[8];
            int hexLen = FormatHex(payload.Length, hexBuffer);
            int totalLen = hexLen + 2 + payload.Length + 2;

            var destination = writer.GetSpan(totalLen)[..totalLen];
            hexBuffer[..hexLen].CopyTo(destination);
            destination[hexLen] = (byte)'\r';
            destination[hexLen + 1] = (byte)'\n';
            payload.CopyTo(destination[(hexLen + 2)..]);
            destination[totalLen - 2] = (byte)'\r';
            destination[totalLen - 1] = (byte)'\n';
            writer.Advance(totalLen);
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

        private static int FormatHex(int value, Span<byte> destination)
        {
            int pos = destination.Length;
            do
            {
                int nibble = value & 0xF;
                destination[--pos] = (byte)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
                value >>= 4;
            }
            while (value > 0);

            int length = destination.Length - pos;
            destination[pos..].CopyTo(destination);
            return length;
        }
    }

    /// <summary>
    /// A write-only <see cref="Stream"/> that passes bytes directly into a <see cref="PipeWriter"/>
    /// without any chunk framing. Used for <c>Connection: close</c> streaming responses where
    /// end-of-body is signalled by TCP close rather than a chunk terminator.
    /// </summary>
    internal sealed class DirectBodyStream(PipeWriter writer) : Stream
    {
        public override bool CanRead  => false;
        public override bool CanSeek  => false;
        public override bool CanWrite => true;
        public override long Length   => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int  Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)        => throw new NotSupportedException();
        public override void SetLength(long value)                        => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count > 0) writer.Write(buffer.AsSpan(offset, count));
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
        {
            if (!buffer.IsEmpty) writer.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void WriteByte(byte value)
        {
            var span = writer.GetSpan(1);
            span[0] = value;
            writer.Advance(1);
        }

        public override void Flush() { } // no-op — use FlushAsync

        public override async Task FlushAsync(CancellationToken ct)
        {
            await writer.FlushAsync(ct);
        }
    }
}
