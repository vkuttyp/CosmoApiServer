using System.Text;
using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Http;

public class ServerSentEventsTests
{
    private static HttpContext MakeContext()
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/events" };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    private static string ResponseText(HttpContext ctx) =>
        Encoding.UTF8.GetString(ctx.Response.Body);

    [Fact]
    public async Task BeginSse_SetsCorrectHeaders()
    {
        var ctx = MakeContext();
        await ctx.Response.BeginSseAsync();

        Assert.Equal("text/event-stream; charset=utf-8", ctx.Response.Headers["Content-Type"]);
        Assert.Equal("no-cache, no-store",               ctx.Response.Headers["Cache-Control"]);
        Assert.Equal("keep-alive",                       ctx.Response.Headers["Connection"]);
        Assert.Equal("no",                               ctx.Response.Headers["X-Accel-Buffering"]);
    }

    [Fact]
    public async Task BeginSse_WritesOpeningComment()
    {
        var ctx = MakeContext();
        await ctx.Response.BeginSseAsync();

        var body = ResponseText(ctx);
        Assert.Contains(": stream-open", body);
    }

    [Fact]
    public async Task WriteSse_SimpleData_FormatsCorrectly()
    {
        var ctx = MakeContext();
        await ctx.Response.WriteSseAsync("hello world");

        var body = ResponseText(ctx);
        Assert.Contains("data: hello world\n", body);
        Assert.EndsWith("\n\n", body);
    }

    [Fact]
    public async Task WriteSse_WithEventName_IncludesEventLine()
    {
        var ctx = MakeContext();
        await ctx.Response.WriteSseAsync("payload", eventName: "update");

        var body = ResponseText(ctx);
        Assert.Contains("event: update\n", body);
        Assert.Contains("data: payload\n", body);
    }

    [Fact]
    public async Task WriteSse_WithId_IncludesIdLine()
    {
        var ctx = MakeContext();
        await ctx.Response.WriteSseAsync("msg", id: "42");

        var body = ResponseText(ctx);
        Assert.Contains("id: 42\n", body);
    }

    [Fact]
    public async Task WriteSse_WithRetry_IncludesRetryLine()
    {
        var ctx = MakeContext();
        await ctx.Response.WriteSseAsync("msg", retry: 3000);

        var body = ResponseText(ctx);
        Assert.Contains("retry: 3000\n", body);
    }

    [Fact]
    public async Task WriteSse_MultilineData_SplitsIntoMultipleDataLines()
    {
        var ctx = MakeContext();
        await ctx.Response.WriteSseAsync("line one\nline two\nline three");

        var body = ResponseText(ctx);
        Assert.Contains("data: line one\n", body);
        Assert.Contains("data: line two\n", body);
        Assert.Contains("data: line three\n", body);
    }

    [Fact]
    public async Task WriteSseComment_WritesCommentLine()
    {
        var ctx = MakeContext();
        await ctx.Response.WriteSseCommentAsync("heartbeat");

        var body = ResponseText(ctx);
        Assert.Contains(": heartbeat\n", body);
    }

    [Fact]
    public async Task MultipleEvents_AllAppearInOrder()
    {
        var ctx = MakeContext();
        await ctx.Response.WriteSseAsync("first",  eventName: "msg");
        await ctx.Response.WriteSseAsync("second", eventName: "msg");
        await ctx.Response.WriteSseAsync("third",  eventName: "msg");

        var body = ResponseText(ctx);
        var firstIdx  = body.IndexOf("data: first",  StringComparison.Ordinal);
        var secondIdx = body.IndexOf("data: second", StringComparison.Ordinal);
        var thirdIdx  = body.IndexOf("data: third",  StringComparison.Ordinal);

        Assert.True(firstIdx < secondIdx);
        Assert.True(secondIdx < thirdIdx);
    }
}
