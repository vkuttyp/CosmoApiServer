using System.Text.Json;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Tests.Controllers;

public class ActionResultTests
{
    private static HttpResponse MakeResponse() => new HttpResponse();

    [Fact]
    public async Task RedirectResult_SetsLocationAndStatusCode()
    {
        var response = MakeResponse();
        var result = new RedirectResult("https://example.com", 301);

        await result.ExecuteAsync(response);

        Assert.Equal(301, response.StatusCode);
        Assert.Equal("https://example.com", response.Headers["Location"]);
    }

    [Fact]
    public async Task FileContentResult_SetsHeadersAndBody()
    {
        var response = MakeResponse();
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var result = new FileContentResult(content, "application/pdf", "test.pdf");

        await result.ExecuteAsync(response);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/pdf", response.Headers["Content-Type"]);
        Assert.Equal("attachment; filename=\"test.pdf\"", response.Headers["Content-Disposition"]);
        Assert.Equal(content, response.Body);
    }
}
