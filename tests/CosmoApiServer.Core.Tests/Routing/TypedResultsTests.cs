using System.Text.Json;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Routing;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Routing;

public class TypedResultsTests
{
    private static HttpContext MakeContext()
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/" };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task Ok_NoValue_Returns200()
    {
        var ctx = MakeContext();
        await TypedResults.Ok()(ctx);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Ok_WithValue_Returns200WithJson()
    {
        var ctx = MakeContext();
        await TypedResults.Ok(new { name = "Alice" })(ctx);
        Assert.Equal(200, ctx.Response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(ctx.Response.Body);
        Assert.Equal("Alice", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Created_SetsLocationHeader()
    {
        var ctx = MakeContext();
        await TypedResults.Created("/items/42", new { id = 42 })(ctx);
        Assert.Equal(201, ctx.Response.StatusCode);
        Assert.Equal("/items/42", ctx.Response.Headers["Location"]);
    }

    [Fact]
    public async Task NoContent_Returns204()
    {
        var ctx = MakeContext();
        await TypedResults.NoContent()(ctx);
        Assert.Equal(204, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task NotFound_Returns404()
    {
        var ctx = MakeContext();
        await TypedResults.NotFound()(ctx);
        Assert.Equal(404, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task BadRequest_WithString_Returns400WithText()
    {
        var ctx = MakeContext();
        await TypedResults.BadRequest("invalid input")(ctx);
        Assert.Equal(400, ctx.Response.StatusCode);
        Assert.Equal("invalid input", System.Text.Encoding.UTF8.GetString(ctx.Response.Body));
    }

    [Fact]
    public async Task Unauthorized_Returns401()
    {
        var ctx = MakeContext();
        await TypedResults.Unauthorized()(ctx);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Forbid_Returns403()
    {
        var ctx = MakeContext();
        await TypedResults.Forbid()(ctx);
        Assert.Equal(403, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Conflict_Returns409()
    {
        var ctx = MakeContext();
        await TypedResults.Conflict(new { error = "duplicate" })(ctx);
        Assert.Equal(409, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Redirect_Sets302AndLocation()
    {
        var ctx = MakeContext();
        await TypedResults.Redirect("/new-url")(ctx);
        Assert.Equal(302, ctx.Response.StatusCode);
        Assert.Equal("/new-url", ctx.Response.Headers["Location"]);
    }

    [Fact]
    public async Task RedirectPermanent_Sets301()
    {
        var ctx = MakeContext();
        await TypedResults.RedirectPermanent("/new-url")(ctx);
        Assert.Equal(301, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Text_WritesPlainText()
    {
        var ctx = MakeContext();
        await TypedResults.Text("hello world")(ctx);
        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal("hello world", System.Text.Encoding.UTF8.GetString(ctx.Response.Body));
    }

    [Fact]
    public async Task Problem_WritesProblemJson()
    {
        var ctx = MakeContext();
        await TypedResults.Problem("Something failed", statusCode: 422)(ctx);
        Assert.Equal(422, ctx.Response.StatusCode);
        Assert.Equal("application/problem+json", ctx.Response.Headers["Content-Type"]);
        var body = JsonSerializer.Deserialize<JsonElement>(ctx.Response.Body);
        Assert.Equal("Something failed", body.GetProperty("title").GetString());
        Assert.Equal(422, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task InternalServerError_Returns500()
    {
        var ctx = MakeContext();
        await TypedResults.InternalServerError()(ctx);
        Assert.Equal(500, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Bytes_WritesBinaryWithContentType()
    {
        var ctx = MakeContext();
        var data = new byte[] { 1, 2, 3, 4 };
        await TypedResults.Bytes(data, "application/octet-stream")(ctx);
        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal("application/octet-stream", ctx.Response.Headers["Content-Type"]);
        Assert.Equal(data, ctx.Response.Body);
    }
}
