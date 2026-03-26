using System.Net.WebSockets;

namespace CosmoApiServer.Core.Http;

/// <summary>
/// A high-performance, minimal WebSocket implementation for CosmoApiServer.
/// Built for speed and low allocations.
/// </summary>
public sealed class CosmoWebSocket(Stream stream) : IDisposable
{
    private readonly Stream _stream = stream;

    public bool IsClosed { get; private set; }

    /// <summary>
    /// Sends a data frame over the WebSocket.
    /// </summary>
    public async Task SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool endOfMessage)
    {
        if (IsClosed) throw new InvalidOperationException("WebSocket is closed.");

        // RFC 6455 Frame Header
        // 0x81 = Text (Fin bit set)
        // 0x82 = Binary (Fin bit set)
        byte header = type == WebSocketMessageType.Text ? (byte)0x81 : (byte)0x82;
        if (!endOfMessage) header &= 0x7F; // Unset Fin bit if not end of message

        var frameHeader = new byte[10];
        frameHeader[0] = header;
        
        int headerSize = 2;
        if (buffer.Length <= 125)
        {
            frameHeader[1] = (byte)buffer.Length;
        }
        else if (buffer.Length <= 65535)
        {
            frameHeader[1] = 126;
            frameHeader[2] = (byte)(buffer.Length >> 8);
            frameHeader[3] = (byte)buffer.Length;
            headerSize = 4;
        }
        else
        {
            frameHeader[1] = 127;
            // Write length as 64-bit integer
            var len = (long)buffer.Length;
            for (int i = 7; i >= 0; i--)
            {
                frameHeader[i + 2] = (byte)(len & 0xFF);
                len >>= 8;
            }
            headerSize = 10;
        }

        await _stream.WriteAsync(frameHeader.AsMemory(0, headerSize));
        await _stream.WriteAsync(buffer);
        await _stream.FlushAsync();
    }

    public async Task<WebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (IsClosed) throw new InvalidOperationException("WebSocket is closed.");

        byte[] header = new byte[2];
        int read = await _stream.ReadAsync(header.AsMemory(0, 2), cancellationToken);
        if (read == 0) { IsClosed = true; return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true); }

        bool fin = (header[0] & 0x80) != 0;
        int opcode = header[0] & 0x0F;
        bool masked = (header[1] & 0x80) != 0;
        long payloadLen = header[1] & 0x7F;

        if (opcode == 0x08) // Close
        {
            IsClosed = true;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        if (payloadLen == 126)
        {
            byte[] extendedLen = new byte[2];
            await _stream.ReadExactlyAsync(extendedLen.AsMemory(), cancellationToken);
            payloadLen = (extendedLen[0] << 8) | extendedLen[1];
        }
        else if (payloadLen == 127)
        {
            byte[] extendedLen = new byte[8];
            await _stream.ReadExactlyAsync(extendedLen.AsMemory(), cancellationToken);
            payloadLen = 0;
            for (int i = 0; i < 8; i++) payloadLen = (payloadLen << 8) | extendedLen[i];
        }

        // RFC 6455 §5.1: server MUST close the connection if a client sends an unmasked frame
        if (!masked)
        {
            IsClosed = true;
            // Close frame with status 1002 (Protocol Error)
            await _stream.WriteAsync(new byte[] { 0x88, 0x02, 0x03, 0xEA }, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        var mask = new byte[4];
        await _stream.ReadExactlyAsync(mask.AsMemory(), cancellationToken);

        int bytesToRead = (int)Math.Min(payloadLen, buffer.Length);
        var targetBuffer = buffer.Span[..bytesToRead];
        
        int totalRead = 0;
        while (totalRead < bytesToRead)
        {
            int r = await _stream.ReadAsync(buffer.Slice(totalRead, bytesToRead - totalRead), cancellationToken);
            if (r == 0) break;
            totalRead += r;
        }

        for (int i = 0; i < totalRead; i++)
            buffer.Span[i] = (byte)(buffer.Span[i] ^ mask[i % 4]);

        var type = (opcode == 0x02 || opcode == 0x00) ? WebSocketMessageType.Binary : WebSocketMessageType.Text;
        return new WebSocketReceiveResult(totalRead, type, fin);
    }

    public async Task CloseAsync(WebSocketCloseStatus status, string? statusDescription)
    {
        if (IsClosed) return;

        // Simplified Close Frame (0x88)
        byte[] closeFrame = [0x88, 0x00];
        try
        {
            await _stream.WriteAsync(closeFrame);
            await _stream.FlushAsync();
        }
        finally
        {
            IsClosed = true;
            _stream.Dispose();
        }
    }

    public void Dispose()
    {
        if (!IsClosed)
        {
            IsClosed = true;
            _stream.Dispose();
        }
    }
}
