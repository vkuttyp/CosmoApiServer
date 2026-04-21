using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Writes HTTP/1.1 requests directly into a <see cref="PipeWriter"/> — no intermediate byte[].
/// Client-side counterpart to <see cref="Http11Writer"/>.
/// </summary>
internal static class Http11RequestWriter
{
    private static readonly byte[] CrLf       = "\r\n"u8.ToArray();
    private static readonly byte[] HeaderSep   = ": "u8.ToArray();
    private static readonly byte[] Http11      = " HTTP/1.1\r\n"u8.ToArray();
    private static readonly byte[] SpaceByte   = " "u8.ToArray();

    /// <summary>
    /// Write a complete HTTP/1.1 request (request line + headers + body) to the pipe.
    /// </summary>
    public static void WriteRequest(
        PipeWriter writer,
        string method,
        string pathAndQuery,
        string host,
        IReadOnlyDictionary<string, string>? headers,
        long contentLength)
    {
        // Request line: METHOD /path HTTP/1.1\r\n
        WriteAscii(writer, method);
        writer.Write(SpaceByte);
        WriteAscii(writer, pathAndQuery);
        writer.Write(Http11);

        // Host header (required for HTTP/1.1)
        writer.Write("Host: "u8);
        WriteAscii(writer, host);
        writer.Write(CrLf);

        // Custom headers
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;

                WriteAscii(writer, name);
                writer.Write(HeaderSep);
                WriteUtf8(writer, value);
                writer.Write(CrLf);
            }
        }

        // Content-Length
        if (contentLength > 0)
        {
            writer.Write("Content-Length: "u8);
            WriteLong(writer, contentLength);
            writer.Write(CrLf);
        }

        // End of headers
        writer.Write(CrLf);
    }

    /// <summary>
    /// Write request body bytes directly into the pipe.
    /// </summary>
    public static void WriteBody(PipeWriter writer, ReadOnlySpan<byte> body)
    {
        if (!body.IsEmpty)
            writer.Write(body);
    }

    /// <summary>
    /// Copy a request body from a PipeReader to the upstream PipeWriter.
    /// Zero-copy when possible — reads from source pipe, writes directly to destination pipe.
    /// </summary>
    public static async Task CopyBodyAsync(PipeWriter writer, PipeReader bodyReader, long contentLength, CancellationToken ct)
    {
        long remaining = contentLength;
        while (remaining > 0)
        {
            var result = await bodyReader.ReadAsync(ct);
            var buffer = result.Buffer;

            if (buffer.IsEmpty && result.IsCompleted)
                break;

            long toCopy = Math.Min(buffer.Length, remaining);
            foreach (var segment in buffer.Slice(0, toCopy))
            {
                writer.Write(segment.Span);
            }

            bodyReader.AdvanceTo(buffer.GetPosition(toCopy));
            remaining -= toCopy;
        }
    }

    private static void WriteAscii(PipeWriter writer, string value)
    {
        int byteCount = Encoding.ASCII.GetByteCount(value);
        Encoding.ASCII.GetBytes(value, writer.GetSpan(byteCount));
        writer.Advance(byteCount);
    }

    private static void WriteUtf8(PipeWriter writer, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(byteCount));
        writer.Advance(byteCount);
    }

    private static void WriteLong(PipeWriter writer, long value)
    {
        Span<byte> buf = stackalloc byte[20];
        int pos = buf.Length;
        if (value == 0) { buf[--pos] = (byte)'0'; }
        else { while (value > 0) { buf[--pos] = (byte)('0' + value % 10); value /= 10; } }
        writer.Write(buf[pos..]);
    }
}
