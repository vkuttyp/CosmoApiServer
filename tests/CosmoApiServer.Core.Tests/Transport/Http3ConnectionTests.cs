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
    public void ParseRequestForTests_RejectsUnknownFrameTypes()
    {
        using var ms = new MemoryStream();
        ms.Write(Http3Connection.EncodeRequestForTests(
        [
            (":method", "POST"),
            (":scheme", "https"),
            (":authority", "localhost"),
            (":path", "/echo")
        ]));
        WriteFrame(ms, 0x09, Encoding.UTF8.GetBytes("bad"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Http3Connection.ParseRequestForTests(ms.ToArray()));

        Assert.Contains("unsupported frame type", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void ParseRequestForTests_RejectsDuplicatePseudoHeaders()
    {
        var encoded = Http3Connection.EncodeFieldSectionForTests(
            (":method", "GET"),
            (":scheme", "https"),
            (":authority", "localhost"),
            (":path", "/one"),
            (":path", "/two"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Http3Connection.ParseRequestForTests(EncodeRequest(encoded)));

        Assert.Contains("duplicate :path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateControlFrameSequenceForTests_RejectsMissingInitialSettings()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Http3Connection.ValidateControlFrameSequenceForTests(0x09));

        Assert.Contains("begin with SETTINGS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateControlFrameSequenceForTests_RejectsDuplicateSettings()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Http3Connection.ValidateControlFrameSequenceForTests(0x04, 0x04));

        Assert.Contains("duplicate SETTINGS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateControlFrameSequenceForTests_RejectsForbiddenFrameTypes()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Http3Connection.ValidateControlFrameSequenceForTests(0x04, 0x01));

        Assert.Contains("control stream", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUnidirectionalStreamSequenceForTests_RejectsDuplicateControlStreams()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Http3Connection.ValidateUnidirectionalStreamSequenceForTests(0x00, 0x00));

        Assert.Contains("duplicate control", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUnidirectionalStreamSequenceForTests_RejectsDuplicateQpackEncoderStreams()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Http3Connection.ValidateUnidirectionalStreamSequenceForTests(0x02, 0x02));

        Assert.Contains("duplicate qpack encoder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUnidirectionalStreamSequenceForTests_RejectsDuplicateQpackDecoderStreams()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Http3Connection.ValidateUnidirectionalStreamSequenceForTests(0x03, 0x03));

        Assert.Contains("duplicate qpack decoder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetermineGoAwayIdForTests_UsesHighestObservedRequestStreamId()
    {
        long id = Http3Connection.DetermineGoAwayIdForTests(0, 4, 12, 8);
        Assert.Equal(12, id);
    }

    [Fact]
    public void DetermineGoAwayIdForTests_UsesMaxValueWhenNoRequestWasObserved()
    {
        long id = Http3Connection.DetermineGoAwayIdForTests();
        Assert.Equal(4611686018427387900L, id);
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

    [Fact]
    public void EncodeResponseHeadersForTests_UsesQpackStaticEntriesWhenAvailable()
    {
        var response = new HttpResponse
        {
            StatusCode = 206
        };
        response.Headers["Accept-Ranges"] = "bytes";
        response.Headers["Content-Type"] = "application/json";

        var encoded = Http3Connection.EncodeResponseHeadersForTests(response);
        var decoded = Http3Connection.DecodeFieldSectionForTests(encoded);

        Assert.Contains(decoded, h => h.name == ":status" && h.value == "206");
        Assert.Contains(decoded, h => h.name == "accept-ranges" && h.value == "bytes");
        Assert.Contains(decoded, h => h.name == "content-type" && h.value == "application/json");

        string encodedAscii = System.Text.Encoding.ASCII.GetString(encoded);
        Assert.DoesNotContain(":status", encodedAscii, StringComparison.Ordinal);
        Assert.DoesNotContain("accept-ranges", encodedAscii, StringComparison.Ordinal);
        Assert.DoesNotContain("content-type", encodedAscii, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeTrailingHeadersForTests_UsesStaticNameReferencesWhenAvailable()
    {
        var encoded = Http3Connection.EncodeTrailingHeadersForTests(new Dictionary<string, string>
        {
            ["Content-Type"] = "text/plain; charset=utf-8",
            ["Content-Length"] = "5"
        });

        var decoded = Http3Connection.DecodeFieldSectionForTests(encoded);

        Assert.Contains(decoded, h => h.name == "content-type" && h.value == "text/plain; charset=utf-8");
        Assert.Contains(decoded, h => h.name == "content-length" && h.value == "5");

        string encodedAscii = System.Text.Encoding.ASCII.GetString(encoded);
        Assert.DoesNotContain("content-type", encodedAscii, StringComparison.Ordinal);
        Assert.DoesNotContain("content-length", encodedAscii, StringComparison.Ordinal);
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
