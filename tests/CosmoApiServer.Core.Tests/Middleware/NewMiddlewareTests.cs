using System.Text.Json;
using System.Net;
using System.Net.Sockets;
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
        // Security: exception details should NOT be exposed to clients
        Assert.False(body.TryGetProperty("detail", out _));
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

    [Fact]
    public async Task SpaFallbackMiddleware_ServesIndexHtml_ForClientRoute()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmoTestSpaFallback");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "index.html"), "<html><body>spa shell</body></html>");

        try
        {
            var middleware = new SpaFallbackMiddleware(new SpaFallbackOptions { RootPath = tempDir });
            var ctx = MakeContext(HttpMethod.GET, "/dashboard/stats");

            await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            Assert.Equal(200, ctx.Response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", ctx.Response.Headers["Content-Type"]);
            Assert.Contains("spa shell", System.Text.Encoding.UTF8.GetString(ctx.Response.Body));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SpaFallbackMiddleware_PassesThrough_ForExcludedApiPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmoTestSpaFallbackExcluded");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "index.html"), "<html><body>spa shell</body></html>");

        try
        {
            var middleware = new SpaFallbackMiddleware(new SpaFallbackOptions { RootPath = tempDir });
            var ctx = MakeContext(HttpMethod.GET, "/api/dashboard");
            var nextCalled = false;

            await middleware.InvokeAsync(ctx, _ =>
            {
                nextCalled = true;
                return ValueTask.CompletedTask;
            });

            Assert.True(nextCalled);
            Assert.Empty(ctx.Response.Body);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SpaFallbackMiddleware_PassesThrough_ForAssetRequests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmoTestSpaFallbackAssets");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "index.html"), "<html><body>spa shell</body></html>");

        try
        {
            var middleware = new SpaFallbackMiddleware(new SpaFallbackOptions { RootPath = tempDir });
            var ctx = MakeContext(HttpMethod.GET, "/assets/app.js");
            var nextCalled = false;

            await middleware.InvokeAsync(ctx, _ =>
            {
                nextCalled = true;
                return ValueTask.CompletedTask;
            });

            Assert.True(nextCalled);
            Assert.Empty(ctx.Response.Body);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ViteFrontendMiddleware_RendersManifestAssets_ForHtmlRoutes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmoTestViteFrontend");
        Directory.CreateDirectory(tempDir);
        var frontendDir = Path.Combine(tempDir, "frontend");
        var viteDir = Path.Combine(tempDir, "wwwroot", ".vite");
        Directory.CreateDirectory(frontendDir);
        Directory.CreateDirectory(viteDir);

        File.WriteAllText(Path.Combine(frontendDir, "index.html"), """
<!doctype html>
<html>
<head><!--app-head--></head>
<body><!--app-html--></body>
</html>
""");

        File.WriteAllText(Path.Combine(viteDir, "manifest.json"), """
{
  "src/main.ts": {
    "file": "assets/app-123.js",
    "css": ["assets/app-123.css"],
    "imports": ["src/chunk.ts"]
  },
  "src/chunk.ts": {
    "file": "assets/chunk-456.js",
    "css": ["assets/chunk-456.css"]
  }
}
""");

        try
        {
            var middleware = new ViteFrontendMiddleware(new ViteFrontendOptions
            {
                HtmlTemplatePath = Path.Combine(frontendDir, "index.html"),
                ManifestPath = Path.Combine(viteDir, "manifest.json")
            });
            var ctx = MakeContext(HttpMethod.GET, "/dashboard");

            await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            var html = System.Text.Encoding.UTF8.GetString(ctx.Response.Body);
            Assert.Contains("/assets/app-123.js", html);
            Assert.Contains("/assets/chunk-456.js", html);
            Assert.Contains("/assets/app-123.css", html);
            Assert.Contains("/assets/chunk-456.css", html);
            Assert.Contains("<div id=\"app\"></div>", html);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ViteFrontendMiddleware_RendersDevServerScripts_WhenConfigured()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmoTestViteFrontendDev");
        Directory.CreateDirectory(tempDir);
        var frontendDir = Path.Combine(tempDir, "frontend");
        Directory.CreateDirectory(frontendDir);

        File.WriteAllText(Path.Combine(frontendDir, "index.html"), """
<!doctype html>
<html>
<head><!--app-head--></head>
<body><!--app-html--></body>
</html>
""");

        try
        {
            var middleware = new ViteFrontendMiddleware(new ViteFrontendOptions
            {
                HtmlTemplatePath = Path.Combine(frontendDir, "index.html"),
                DevServerUrl = "http://localhost:5173"
            });
            var ctx = MakeContext(HttpMethod.GET, "/");

            await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            var html = System.Text.Encoding.UTF8.GetString(ctx.Response.Body);
            Assert.Contains("http://localhost:5173/@vite/client", html);
            Assert.Contains("http://localhost:5173/src/main.ts", html);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ViteFrontendMiddleware_IncludesRenderHookHeadAndState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmoTestViteFrontendState");
        Directory.CreateDirectory(tempDir);
        var frontendDir = Path.Combine(tempDir, "frontend");
        var viteDir = Path.Combine(tempDir, "wwwroot", ".vite");
        Directory.CreateDirectory(frontendDir);
        Directory.CreateDirectory(viteDir);

        File.WriteAllText(Path.Combine(frontendDir, "index.html"), """
<!doctype html>
<html>
<head><!--app-head--></head>
<body><!--app-html--><!--app-state--><!--app-body-end--></body>
</html>
""");

        File.WriteAllText(Path.Combine(viteDir, "manifest.json"), """
{
  "src/main.ts": {
    "file": "assets/app-123.js"
  }
}
""");

        try
        {
            var middleware = new ViteFrontendMiddleware(new ViteFrontendOptions
            {
                HtmlTemplatePath = Path.Combine(frontendDir, "index.html"),
                ManifestPath = Path.Combine(viteDir, "manifest.json"),
                RenderAsync = ctx => ValueTask.FromResult<ViteRenderResult?>(new ViteRenderResult
                {
                    HeadHtml = "<title>SSR title</title>",
                    AppHtml = "<div id=\"app\">server shell</div>",
                    InitialState = new { route = ctx.HttpContext.Request.Path, ok = true },
                    BodyEndHtml = "<!--tail-->"
                })
            });
            var ctx = MakeContext(HttpMethod.GET, "/dashboard");

            await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            var html = System.Text.Encoding.UTF8.GetString(ctx.Response.Body);
            Assert.Contains("<title>SSR title</title>", html);
            Assert.Contains("<div id=\"app\">server shell</div>", html);
            Assert.Contains("window.__COSMO_VITE_STATE__", html);
            Assert.Contains("\"route\":\"/dashboard\"", html);
            Assert.Contains("<!--tail-->", html);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ViteFrontendMiddleware_UsesExternalSsrEndpoint_WhenConfigured()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmoTestViteFrontendExternalSsr");
        Directory.CreateDirectory(tempDir);
        var frontendDir = Path.Combine(tempDir, "frontend");
        var viteDir = Path.Combine(tempDir, "wwwroot", ".vite");
        Directory.CreateDirectory(frontendDir);
        Directory.CreateDirectory(viteDir);

        File.WriteAllText(Path.Combine(frontendDir, "index.html"), """
<!doctype html>
<html>
<head><!--app-head--></head>
<body><!--app-html--><!--app-state--></body>
</html>
""");

        File.WriteAllText(Path.Combine(viteDir, "manifest.json"), """
{
  "src/main.ts": {
    "file": "assets/app-123.js"
  }
}
""");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);

            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
            }

            var payload = JsonSerializer.Serialize(new
            {
                headHtml = "<title>Bridge Title</title>",
                appHtml = "<div id=\"app\">bridge html</div>",
                initialState = new { route = "/bridge" }
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {payload.Length}\r\n" +
                "Connection: close\r\n\r\n" +
                payload);
            await stream.WriteAsync(bytes);
        });

        try
        {
            var middleware = new ViteFrontendMiddleware(new ViteFrontendOptions
            {
                HtmlTemplatePath = Path.Combine(frontendDir, "index.html"),
                ManifestPath = Path.Combine(viteDir, "manifest.json"),
                SsrEndpointUrl = $"http://127.0.0.1:{port}/__cosmo/ssr"
            });
            var ctx = MakeContext(HttpMethod.GET, "/bridge");

            await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

            var html = System.Text.Encoding.UTF8.GetString(ctx.Response.Body);
            Assert.Contains("<title>Bridge Title</title>", html);
            Assert.Contains("<div id=\"app\">bridge html</div>", html);
            Assert.Contains("\"route\":\"/bridge\"", html);
        }
        finally
        {
            listener.Stop();
            await serverTask;
            Directory.Delete(tempDir, true);
        }
    }
}
