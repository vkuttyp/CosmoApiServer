using System.Net.WebSockets;
using System.Text;
using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Tests.Http;

public class WebSocketTests
{
    [Fact]
    public void WebSocketHelper_CreatesCorrectResponseKey()
    {
        // Example from RFC 6455
        var requestKey = "dGhlIHNhbXBsZSBub25jZQ==";
        var expected = "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=";
        
        var result = WebSocketHelper.CreateResponseKey(requestKey);
        
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsWebSocketRequest_ReturnsTrue_ForValidHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Connection", "Upgrade" },
            { "Upgrade", "websocket" }
        };
        var req = new HttpRequest { Headers = headers };
        
        Assert.True(WebSocketHelper.IsWebSocketRequest(req));
    }

    [Fact]
    public async Task SendAsync_WritesCorrectFrameHeader_SmallText()
    {
        var ms = new MemoryStream();
        using var ws = new CosmoWebSocket(ms);
        var data = Encoding.UTF8.GetBytes("Hello");

        await ws.SendAsync(data, WebSocketMessageType.Text, true);

        var result = ms.ToArray();
        
        // 0x81 = Text, Fin
        Assert.Equal(0x81, result[0]);
        // Length = 5
        Assert.Equal(5, result[1]);
        // Data starts at index 2
        Assert.Equal("Hello", Encoding.UTF8.GetString(result[2..]));
    }

    [Fact]
    public async Task SendAsync_WritesCorrectFrameHeader_MediumBinary()
    {
        var ms = new MemoryStream();
        using var ws = new CosmoWebSocket(ms);
        var data = new byte[200];

        await ws.SendAsync(data, WebSocketMessageType.Binary, true);

        var result = ms.ToArray();
        
        // 0x82 = Binary, Fin
        Assert.Equal(0x82, result[0]);
        // 126 = Medium payload marker
        Assert.Equal(126, result[1]);
        // Length 200 in 16-bit big endian
        Assert.Equal(0, result[2]);
        Assert.Equal(200, result[3]);
    }

    [Fact]
    public async Task ReceiveAsync_ReadsCorrectFrame_SmallText()
    {
        // 0x81 (Text, Fin), 0x85 (Masked, Length 5), mask 0x00*4 (XOR identity), payload "Hello"
        var data = new byte[] { 0x81, 0x85, 0x00, 0x00, 0x00, 0x00, (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
        var ms = new MemoryStream(data);
        using var ws = new CosmoWebSocket(ms);
        var buffer = new byte[10];

        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        Assert.Equal("Hello", Encoding.UTF8.GetString(buffer, 0, 5));
    }

    [Fact]
    public async Task ReceiveAsync_UnmasksPayload()
    {
        // 0x81 (Text, Fin), 0x85 (Masked, Length 5)
        // Mask: 0x01 0x02 0x03 0x04
        // Data: "Hello"
        // H (0x48) ^ 0x01 = 0x49
        // e (0x65) ^ 0x02 = 0x67
        // l (0x6C) ^ 0x03 = 0x6F
        // l (0x6C) ^ 0x04 = 0x68
        // o (0x6F) ^ 0x01 = 0x6E
        var data = new byte[] { 0x81, 0x85, 0x01, 0x02, 0x03, 0x04, 0x49, 0x67, 0x6F, 0x68, 0x6E };
        var ms = new MemoryStream(data);
        using var ws = new CosmoWebSocket(ms);
        var buffer = new byte[10];

        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.Equal("Hello", Encoding.UTF8.GetString(buffer, 0, 5));
    }
}
