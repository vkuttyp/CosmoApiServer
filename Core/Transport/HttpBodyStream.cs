using System.Buffers;
using System.IO.Pipelines;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// A Stream that reads from a PipeReader, limited to a certain length or handling chunked encoding.
/// </summary>
internal sealed class HttpBodyStream : Stream
{
    private readonly PipeReader _reader;
    private long _remaining;
    private readonly bool _chunked;
    private bool _eof;
    private bool _disposed;

    public HttpBodyStream(PipeReader reader, long length, bool chunked)
    {
        _reader = reader;
        _remaining = length;
        _chunked = chunked;
        _eof = !chunked && length <= 0;
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_eof || _disposed) return 0;

        if (_chunked)
        {
            return await ReadChunkedAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
        }
        else
        {
            return await ReadFixedAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
        }
    }

    private async Task<int> ReadFixedAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (_remaining <= 0)
        {
            _eof = true;
            return 0;
        }

        int toRead = (int)Math.Min(buffer.Length, _remaining);
        var result = await _reader.ReadAsync(ct);
        var seq = result.Buffer;

        if (seq.IsEmpty && result.IsCompleted)
        {
            _eof = true;
            return 0;
        }

        int actualRead = (int)Math.Min(seq.Length, toRead);
        seq.Slice(0, actualRead).CopyTo(buffer.Span);

        _reader.AdvanceTo(seq.GetPosition(actualRead));
        _remaining -= actualRead;

        if (_remaining <= 0) _eof = true;

        return actualRead;
    }

    private async Task<int> ReadChunkedAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (_remaining == 0) // We use _remaining to track current chunk remaining size
        {
            // Read next chunk size
            while (true)
            {
                var result = await _reader.ReadAsync(ct);
                var seq = result.Buffer;
                var reader = new SequenceReader<byte>(seq);

                if (reader.TryReadTo(out ReadOnlySequence<byte> line, [ (byte)'\r', (byte)'\n' ]))
                {
                    if (TryParseHex(line, out long chunkSize))
                    {
                        // Consume past the CRLF delimiter (line.End points before \r\n, advance 2 more bytes)
                        _reader.AdvanceTo(seq.GetPosition(2, line.End));
                        _remaining = chunkSize;
                        if (chunkSize == 0)
                        {
                            _eof = true;
                            // Need to consume the final CRLF after the 0 chunk
                            var finalResult = await _reader.ReadAsync(ct);
                            if (finalResult.Buffer.Length >= 2)
                                _reader.AdvanceTo(finalResult.Buffer.GetPosition(2));
                            return 0;
                        }
                        break;
                    }
                }

                _reader.AdvanceTo(seq.Start, seq.End);
                if (result.IsCompleted) { _eof = true; return 0; }
            }
        }

        // Read from current chunk
        int toRead = (int)Math.Min(buffer.Length, _remaining);
        var readResult = await _reader.ReadAsync(ct);
        var readSeq = readResult.Buffer;

        if (readSeq.IsEmpty && readResult.IsCompleted)
        {
            _eof = true;
            return 0;
        }

        int actualRead = (int)Math.Min(readSeq.Length, toRead);
        readSeq.Slice(0, actualRead).CopyTo(buffer.Span);

        _remaining -= actualRead;
        
        if (_remaining == 0)
        {
            // End of chunk, need to skip CRLF
            if (readSeq.Length >= actualRead + 2)
            {
                _reader.AdvanceTo(readSeq.GetPosition(actualRead + 2));
            }
            else
            {
                _reader.AdvanceTo(readSeq.GetPosition(actualRead));
                // Read and skip CRLF in next turn or now
                var crlfResult = await _reader.ReadAsync(ct);
                if (crlfResult.Buffer.Length >= 2)
                    _reader.AdvanceTo(crlfResult.Buffer.GetPosition(2));
            }
        }
        else
        {
            _reader.AdvanceTo(readSeq.GetPosition(actualRead));
        }

        return actualRead;
    }

    private static bool TryParseHex(ReadOnlySequence<byte> sequence, out long result)
    {
        result = 0;
        if (sequence.IsEmpty) return false;
        foreach (var memory in sequence)
        {
            foreach (byte b in memory.Span)
            {
                if (b == (byte)';') return result >= 0; // chunk extension delimiter
                if (result > (long.MaxValue >> 4)) return false; // overflow protection
                result <<= 4;
                if (b >= '0' && b <= '9') result += b - '0';
                else if (b >= 'a' && b <= 'f') result += b - 'a' + 10;
                else if (b >= 'A' && b <= 'F') result += b - 'A' + 10;
                else return false;
            }
        }
        return result >= 0;
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
