using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Transport;

namespace CosmoApiServer.Core.Tests.Http;

public class StreamingTests
{
    [Fact]
    public async Task WriteStreamingResponseAsync_WritesNdjson()
    {
        var pipe = new Pipe();
        var writer = pipe.Writer;
        var reader = pipe.Reader;

        async Task WriteItems(Stream stream)
        {
            var items = new[] { new { id = 1 }, new { id = 2 } };
            foreach (var item in items)
            {
                await JsonSerializer.SerializeAsync(stream, item);
                stream.WriteByte((byte)'\n');
            }
        }

        var task = Http11Writer.WriteStreamingResponseAsync(writer, 200, WriteItems, CancellationToken.None);
        await task;
        writer.Complete();

        var content = await ReadFullPipeAsync(reader);

        Assert.Contains("HTTP/1.1 200 OK", content);
        Assert.Contains("Transfer-Encoding: chunked", content);
        Assert.Contains("Content-Type: application/x-ndjson", content);

        // Items encoded as chunks
        Assert.Contains("{\"id\":1}", content);
        Assert.Contains("{\"id\":2}", content);
        // Chunk terminator
        Assert.Contains("0\r\n\r\n", content);
    }

    [Fact]
    public async Task SendFileAsync_StreamingPath_WritesToBodyWriter()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "stream-test-" + Guid.NewGuid().ToString() + ".txt");
        var content = "Streaming file content test data for zero-copy path.";
        File.WriteAllText(tempFile, content);

        try
        {
            var pipe = new Pipe();
            var response = new HttpResponse();
            response.BodyWriter = pipe.Writer; // Trigger streaming path

            var sendTask = response.SendFileAsync(tempFile);
            await sendTask;
            pipe.Writer.Complete();

            var result = await ReadFullPipeAsync(pipe.Reader);
            
            // Should contain headers + content
            Assert.Contains("HTTP/1.1 200 OK", result);
            Assert.Contains(content, result);
            Assert.True(response.IsStarted);
            Assert.Equal(content.Length.ToString(), response.Headers["Content-Length"]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private async Task<string> ReadFullPipeAsync(PipeReader reader)
    {
        var sb = new StringBuilder();
        while (true)
        {
            var result = await reader.ReadAsync();
            var buffer = result.Buffer;
            if (buffer.Length > 0)
            {
                sb.Append(Encoding.UTF8.GetString(buffer.ToArray()));
            }
            reader.AdvanceTo(buffer.End);
            if (result.IsCompleted) break;
        }
        return sb.ToString();
    }
}
