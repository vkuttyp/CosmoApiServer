using System.Text.Json;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class NewMiddlewareTests
{
    private static HttpContext MakeContext(HttpMethod method = HttpMethod.GET, string path = "/test")
    {
        var req = new HttpRequest { Method = method, Path = path };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task ExceptionHandler_CatchesException_Returns500()
    {
        var middleware = new GlobalExceptionHandlerMiddleware();
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, _ => throw new Exception("Test exception"));

        Assert.Equal(500, ctx.Response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(ctx.Response.Body);
        Assert.Equal("An unexpected error occurred.", body.GetProperty("message").GetString());
        Assert.Equal("Test exception", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task StaticFileMiddleware_ServesExistingFile()
    {
        // Setup a temporary directory and file
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmoTestStatic");
        Directory.CreateDirectory(tempDir);
        var testFile = Path.Combine(tempDir, "test.txt");
        File.WriteAllText(testFile, "Hello World!");

        try
        {
            var middleware = new StaticFileMiddleware(tempDir);
            var ctx = MakeContext(HttpMethod.GET, "/test.txt");

            await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            Assert.Equal(200, ctx.Response.StatusCode);
            Assert.Equal("text/plain", ctx.Response.Headers["Content-Type"]);
            Assert.Equal("Hello World!", System.Text.Encoding.UTF8.GetString(ctx.Response.Body));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StaticFileMiddleware_PassesThrough_WhenFileNotFound()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmoTestStaticEmpty");
        Directory.CreateDirectory(tempDir);

        try
        {
            var middleware = new StaticFileMiddleware(tempDir);
            var ctx = MakeContext(HttpMethod.GET, "/notfound.txt");
            var nextCalled = false;

            await middleware.InvokeAsync(ctx, _ =>
            {
                nextCalled = true;
                return ValueTask.CompletedTask;
            });

            Assert.True(nextCalled);
            Assert.Equal(200, ctx.Response.StatusCode); // Default response status is 200, middleware should not have touched it
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
