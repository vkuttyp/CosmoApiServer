using System.Text.Json;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.ProblemDetails;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class ProblemDetailsTests
{
    private static HttpContext MakeContext(int statusCode = 500)
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/test" };
        var res = new HttpResponse { StatusCode = statusCode };
        var services = new ServiceCollection();
        var opts = new ProblemDetailsOptions();
        services.AddSingleton(opts);
        services.AddSingleton<IProblemDetailsService, DefaultProblemDetailsService>();
        return new HttpContext(req, res, services.BuildServiceProvider());
    }

    [Fact]
    public async Task ProblemDetails_Writes_ApplicationProblemJson()
    {
        var ctx = MakeContext(404);
        var svc = ctx.RequestServices.GetRequiredService<IProblemDetailsService>();

        await svc.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            ProblemDetails = new CosmoApiServer.Core.ProblemDetails.ProblemDetails { Status = 404 }
        });

        Assert.Equal("application/problem+json", ctx.Response.Headers["Content-Type"]);
        var body = JsonSerializer.Deserialize<JsonElement>(ctx.Response.Body);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ProblemDetails_500_HasCorrectTitle()
    {
        var ctx = MakeContext(500);
        var svc = ctx.RequestServices.GetRequiredService<IProblemDetailsService>();

        await svc.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            ProblemDetails = new CosmoApiServer.Core.ProblemDetails.ProblemDetails { Status = 500 }
        });

        var body = JsonSerializer.Deserialize<JsonElement>(ctx.Response.Body);
        Assert.Equal("Internal Server Error", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ExceptionHandler_UsesProblemDetails_WhenRegistered()
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = "/test" };
        var res = new HttpResponse();
        var services = new ServiceCollection();
        services.AddSingleton(new ProblemDetailsOptions());
        services.AddSingleton<IProblemDetailsService, DefaultProblemDetailsService>();
        var ctx = new HttpContext(req, res, services.BuildServiceProvider());

        var middleware = new GlobalExceptionHandlerMiddleware();
        await middleware.InvokeAsync(ctx, _ => throw new InvalidOperationException("boom"));

        Assert.Equal(500, ctx.Response.StatusCode);
        Assert.Equal("application/problem+json", ctx.Response.Headers["Content-Type"]);
    }

    [Fact]
    public void ProblemDetails_TypeForStatus_KnownCodes()
    {
        Assert.NotEqual("about:blank", CosmoApiServer.Core.ProblemDetails.ProblemDetails.TypeForStatus(404));
        Assert.NotEqual("about:blank", CosmoApiServer.Core.ProblemDetails.ProblemDetails.TypeForStatus(500));
    }
}
