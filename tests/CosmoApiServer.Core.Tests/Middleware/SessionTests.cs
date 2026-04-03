using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class SessionTests
{
    private static HttpContext MakeContext(string? sessionCookie = null)
    {
        var headers = new Dictionary<string, string>();
        if (sessionCookie is not null)
            headers["Cookie"] = $".cosmo.session={sessionCookie}";
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/test", Headers = headers };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task Session_NewRequest_CreatesSession()
    {
        var middleware = new SessionMiddleware(new SessionOptions());
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.NotNull(ctx.Session);
        Assert.True(ctx.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task Session_SetAndGet_String()
    {
        var middleware = new SessionMiddleware(new SessionOptions());
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, c =>
        {
            c.Session!.SetString("username", "alice");
            Assert.Equal("alice", c.Session.GetString("username"));
            return ValueTask.CompletedTask;
        });
    }

    [Fact]
    public async Task Session_SetAndGet_Int32()
    {
        var middleware = new SessionMiddleware(new SessionOptions());
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, c =>
        {
            c.Session!.SetInt32("count", 42);
            Assert.Equal(42, c.Session.GetInt32("count"));
            return ValueTask.CompletedTask;
        });
    }

    [Fact]
    public async Task Session_Remove_ClearsKey()
    {
        var middleware = new SessionMiddleware(new SessionOptions());
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, c =>
        {
            c.Session!.SetString("key", "value");
            c.Session.Remove("key");
            Assert.Null(c.Session.GetString("key"));
            return ValueTask.CompletedTask;
        });
    }

    [Fact]
    public async Task Session_Clear_RemovesAll()
    {
        var middleware = new SessionMiddleware(new SessionOptions());
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, c =>
        {
            c.Session!.SetString("a", "1");
            c.Session.SetString("b", "2");
            c.Session.Clear();
            Assert.Null(c.Session.GetString("a"));
            Assert.Null(c.Session.GetString("b"));
            return ValueTask.CompletedTask;
        });
    }
}
