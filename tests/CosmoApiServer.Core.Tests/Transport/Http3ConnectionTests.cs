using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Transport;

namespace CosmoApiServer.Core.Tests.Transport;

public class Http3ConnectionTests
{
    [Fact]
    public void ParseRequestForTests_ParsesBasicGetRequest()
    {
        var requestBytes = Http3Connection.EncodeRequestForTests(
        [
            (":method", "GET"),
            (":scheme", "https"),
            (":authority", "localhost"),
            (":path", "/ping?x=1"),
            ("accept", "*/*")
        ]);

        var parsed = Http3Connection.ParseRequestForTests(requestBytes);

        Assert.Equal("GET", parsed.Method);
        Assert.Equal("/ping", parsed.Path);
        Assert.Equal("x=1", parsed.QueryString);
        Assert.Equal("localhost", parsed.Host);
        Assert.Equal("*/*", parsed.Headers["accept"]);
        Assert.Equal("localhost", parsed.Headers["host"]);
        Assert.Empty(parsed.Body);
    }

    [Fact]
    public void ParseRequestForTests_ParsesPostBody()
    {
        var body = Encoding.UTF8.GetBytes("{\"id\":1}");
        var requestBytes = Http3Connection.EncodeRequestForTests(
        [
            (":method", "POST"),
            (":scheme", "https"),
            (":authority", "localhost"),
            (":path", "/echo"),
            ("content-type", "application/json")
        ], body);

        var parsed = Http3Connection.ParseRequestForTests(requestBytes);

        Assert.Equal("POST", parsed.Method);
        Assert.Equal("/echo", parsed.Path);
        Assert.Equal("application/json", parsed.Headers["content-type"]);
        Assert.Equal(body, parsed.Body);
    }

    [Fact]
    public void ParseRequestForTests_CombinesMultipleDataFrames()
    {
        var body1 = Encoding.UTF8.GetBytes("hello ");
        var body2 = Encoding.UTF8.GetBytes("world");
        using var ms = new MemoryStream();
        ms.Write(Http3Connection.EncodeRequestForTests(
        [
            (":method", "POST"),
            (":scheme", "https"),
            (":authority", "localhost"),
            (":path", "/echo")
        ]));

        WriteFrame(ms, 0x00, body1);
        WriteFrame(ms, 0x00, body2);

        var parsed = Http3Connection.ParseRequestForTests(ms.ToArray());

        Assert.Equal("hello world", Encoding.UTF8.GetString(parsed.Body));
    }

    [Fact]
    public void DecodeFieldSectionForTests_RejectsPseudoHeadersAfterRegularHeaders()
    {
        var encoded = Http3Connection.EncodeFieldSectionForTests(
            ("accept", "*/*"),
            (":method", "GET"),
            (":path", "/"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Http3Connection.ParseRequestForTests(EncodeRequest(encoded)));

        Assert.Contains("pseudo headers", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EncodeResponseHeadersForTests_EncodesStatusAndHeaders()
    {
        var response = new HttpResponse
        {
            StatusCode = 200
        };
        response.Headers["Content-Type"] = "text/plain; charset=utf-8";
        response.Headers["Content-Length"] = "4";

        var encoded = Http3Connection.EncodeResponseHeadersForTests(response);
        var decoded = Http3Connection.DecodeFieldSectionForTests(encoded);

        Assert.Contains(decoded, h => h.name == ":status" && h.value == "200");
        Assert.Contains(decoded, h => h.name == "content-type" && h.value == "text/plain; charset=utf-8");
        Assert.Contains(decoded, h => h.name == "content-length" && h.value == "4");
    }

    private static byte[] EncodeRequest(byte[] fieldSection)
    {
        using var ms = new MemoryStream();
        WriteFrame(ms, 0x01, fieldSection);
        return ms.ToArray();
    }

    private static void WriteFrame(Stream stream, long type, byte[] payload)
    {
        WriteVarInt(stream, type);
        WriteVarInt(stream, payload.LongLength);
        stream.Write(payload, 0, payload.Length);
    }

    private static void WriteVarInt(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        int length = EncodeVarInt(value, buffer);
        stream.Write(buffer[..length]);
    }

    private static int EncodeVarInt(long value, Span<byte> destination)
    {
        if (value < 64)
        {
            destination[0] = (byte)value;
            return 1;
        }
        if (value < 16384)
        {
            destination[0] = (byte)(0x40 | ((value >> 8) & 0x3F));
            destination[1] = (byte)(value & 0xFF);
            return 2;
        }
        if (value < 1073741824)
        {
            destination[0] = (byte)(0x80 | ((value >> 24) & 0x3F));
            destination[1] = (byte)((value >> 16) & 0xFF);
            destination[2] = (byte)((value >> 8) & 0xFF);
            destination[3] = (byte)(value & 0xFF);
            return 4;
        }

        destination[0] = (byte)(0xC0 | ((value >> 56) & 0x3F));
        destination[1] = (byte)((value >> 48) & 0xFF);
        destination[2] = (byte)((value >> 40) & 0xFF);
        destination[3] = (byte)((value >> 32) & 0xFF);
        destination[4] = (byte)((value >> 24) & 0xFF);
        destination[5] = (byte)((value >> 16) & 0xFF);
        destination[6] = (byte)((value >> 8) & 0xFF);
        destination[7] = (byte)(value & 0xFF);
        return 8;
    }
}
