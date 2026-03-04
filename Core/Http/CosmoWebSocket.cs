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
