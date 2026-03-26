using System.Buffers;
using System.Text;
using CosmoApiServer.Core.Transport;

namespace CosmoApiServer.Core.Tests.Transport;

public class PipeliningTests
{
    [Fact]
    public void TryParse_HandlesPipelinedRequests()
    {
        var raw = "GET /first HTTP/1.1\r\nHost: localhost\r\n\r\n" +
                  "GET /second HTTP/1.1\r\nHost: localhost\r\n\r\n";
        
        var buffer = new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(raw));
        
        // Match first request
        bool first = Http11Parser.TryParse(ref buffer, out var req1);
        Assert.True(first);
        Assert.Equal("GET", req1.Method);
        Assert.Equal("/first", req1.RawTarget);
        
        // Match second request from remaining buffer
        bool second = Http11Parser.TryParse(ref buffer, out var req2);
        Assert.True(second);
        Assert.Equal("GET", req2.Method);
        Assert.Equal("/second", req2.RawTarget);
        
        // No more requests
        Assert.True(buffer.IsEmpty);
        bool third = Http11Parser.TryParse(ref buffer, out _);
        Assert.False(third);
    }

    [Fact]
    public void TryParse_HandlesIncompleteRequest()
    {
        var raw = "GET /path HTTP/1.1\r\nHost: local"; // Missing \r\n\r\n
        var originalBuffer = new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(raw));
        var buffer = originalBuffer;

        bool parsed = Http11Parser.TryParse(ref buffer, out _);
        
        Assert.False(parsed);
        // Buffer should remain at the start if parsing failed due to incompleteness
        Assert.Equal(originalBuffer.Start, buffer.Start);
    }
}
