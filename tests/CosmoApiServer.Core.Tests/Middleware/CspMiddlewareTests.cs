using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class CspMiddlewareTests
{
    private static HttpContext MakeContext()
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/" };
        var res = new HttpResponse();
        return new HttpContext(req, res, new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task Csp_SetsContentSecurityPolicyHeader()
    {
        var middleware = new CspMiddleware(new CspOptions());
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.True(ctx.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    [Fact]
    public async Task Csp_ReportOnly_SetsReportOnlyHeader()
    {
        var middleware = new CspMiddleware(new CspOptions { ReportOnly = true });
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.True(ctx.Response.Headers.ContainsKey("Content-Security-Policy-Report-Only"));
        Assert.False(ctx.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    [Fact]
    public async Task Csp_GeneratesUniqueNoncePerRequest()
    {
        var middleware = new CspMiddleware(new CspOptions());

        string? nonce1 = null, nonce2 = null;

        var ctx1 = MakeContext();
        await middleware.InvokeAsync(ctx1, _ =>
        {
            nonce1 = ctx1.Items[CspMiddleware.NonceItemKey] as string;
            return ValueTask.CompletedTask;
        });

        var ctx2 = MakeContext();
        await middleware.InvokeAsync(ctx2, _ =>
        {
            nonce2 = ctx2.Items[CspMiddleware.NonceItemKey] as string;
            return ValueTask.CompletedTask;
        });

        Assert.NotNull(nonce1);
        Assert.NotNull(nonce2);
        Assert.NotEqual(nonce1, nonce2);
    }

    [Fact]
    public async Task Csp_NonceIsEmbeddedInScriptSrcDirective()
    {
        var middleware = new CspMiddleware(new CspOptions
        {
            ScriptSrc = ["'self'", "'nonce-{nonce}'"]
        });
        var ctx = MakeContext();
        string? capturedNonce = null;

        await middleware.InvokeAsync(ctx, _ =>
        {
            capturedNonce = ctx.Items[CspMiddleware.NonceItemKey] as string;
            return ValueTask.CompletedTask;
        });

        var policy = ctx.Response.Headers["Content-Security-Policy"];
        Assert.Contains($"'nonce-{capturedNonce}'", policy);
        Assert.DoesNotContain("{nonce}", policy);
    }

    [Fact]
    public async Task Csp_PolicyContainsAllConfiguredDirectives()
    {
        var middleware = new CspMiddleware(new CspOptions
        {
            DefaultSrc     = ["'self'"],
            ScriptSrc      = ["'self'"],
            StyleSrc       = ["'self'"],
            ImgSrc         = ["'self'", "data:"],
            FrameAncestors = ["'none'"],
            FormAction     = ["'self'"],
        });
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        var policy = ctx.Response.Headers["Content-Security-Policy"];
        Assert.Contains("default-src", policy);
        Assert.Contains("script-src",  policy);
        Assert.Contains("style-src",   policy);
        Assert.Contains("img-src",     policy);
        Assert.Contains("frame-ancestors", policy);
        Assert.Contains("form-action", policy);
    }

    [Fact]
    public async Task Csp_NextIsCalled()
    {
        var middleware = new CspMiddleware(new CspOptions());
        var ctx = MakeContext();
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Csp_ExtraDirectives_AreAppendedVerbatim()
    {
        var middleware = new CspMiddleware(new CspOptions
        {
            Extra = ["upgrade-insecure-requests", "block-all-mixed-content"]
        });
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        var policy = ctx.Response.Headers["Content-Security-Policy"];
        Assert.Contains("upgrade-insecure-requests", policy);
        Assert.Contains("block-all-mixed-content",   policy);
    }
}
