using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Tests.Http;

public class HttpResponseTests
{
    [Fact]
    public void WriteText_SetsBodyAndContentType()
    {
        var response = new HttpResponse();
        response.WriteText("hello");

        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(response.Body));
        Assert.Contains("text/plain", response.Headers["Content-Type"]);
    }

    [Fact]
    public void WriteJson_SetsBodyAndJsonContentType()
    {
        var response = new HttpResponse();
        response.WriteJson(new { value = 42 });

        Assert.Contains("application/json", response.Headers["Content-Type"]);
        Assert.Contains("42", System.Text.Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public void DefaultStatusCode_Is200()
    {
        var response = new HttpResponse();
        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public void IsStarted_FalseBeforeWrite()
    {
        var response = new HttpResponse();
        Assert.False(response.IsStarted);
    }

    [Fact]
    public async Task SendFileAsync_BuffersToBody()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "stream-test-" + Guid.NewGuid().ToString() + ".txt");
        var content = "Streaming file content test data.";
        File.WriteAllText(tempFile, content);

        try
        {
            var response = new HttpResponse();
            // No BodyWriter set -> should use buffering path
            await response.SendFileAsync(tempFile);

            var body = System.Text.Encoding.UTF8.GetString(response.Body);
            Assert.Equal(content, body);
            Assert.Equal(content.Length.ToString(), response.Headers["Content-Length"]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsStarted_TrueAfterWrite()
    {
        var response = new HttpResponse();
        response.WriteText("hello");
        Assert.True(response.IsStarted);
    }
}
