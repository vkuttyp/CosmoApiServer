using System.Text.Json;
using CosmoApiServer.Core.HealthChecks;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Middleware;

public class HealthCheckTests
{
    private static HttpContext MakeContext(string path = "/health")
    {
        var req = new HttpRequest { Method = HttpMethod.GET, Path = path };
        var res = new HttpResponse();
        var services = new ServiceCollection();
        var service = new HealthCheckService();
        services.AddSingleton(service);
        return new HttpContext(req, res, services.BuildServiceProvider());
    }

    [Fact]
    public async Task HealthCheck_NoChecks_Returns200Healthy()
    {
        var ctx = MakeContext();
        var middleware = new HealthCheckMiddleware("/health");

        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(200, ctx.Response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(ctx.Response.Body);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthCheck_AllHealthy_Returns200()
    {
        var ctx = MakeContext();
        var service = ctx.RequestServices.GetRequiredService<HealthCheckService>();
        service.Register("db", new LambdaHealthCheck(async _ =>
            await Task.FromResult(HealthCheckResult.Healthy("Connected"))));

        var middleware = new HealthCheckMiddleware("/health");
        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(200, ctx.Response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(ctx.Response.Body);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthCheck_UnhealthyCheck_Returns503()
    {
        var ctx = MakeContext();
        var service = ctx.RequestServices.GetRequiredService<HealthCheckService>();
        service.Register("db", new LambdaHealthCheck(async _ =>
            await Task.FromResult(HealthCheckResult.Unhealthy("Connection refused"))));

        var middleware = new HealthCheckMiddleware("/health");
        await middleware.InvokeAsync(ctx, _ => ValueTask.CompletedTask);

        Assert.Equal(503, ctx.Response.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(ctx.Response.Body);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthCheck_DifferentPath_PassesThrough()
    {
        var ctx = MakeContext("/other");
        var middleware = new HealthCheckMiddleware("/health");
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HealthCheckService_MixedResults_ReturnsWorstStatus()
    {
        var service = new HealthCheckService();
        service.Register("ok", new LambdaHealthCheck(async _ => await Task.FromResult(HealthCheckResult.Healthy())));
        service.Register("degraded", new LambdaHealthCheck(async _ => await Task.FromResult(HealthCheckResult.Degraded("Slow"))));

        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, report.Status);
    }

    [Fact]
    public async Task HealthChecksBuilder_AddCheck_RegistersCheck()
    {
        var service = new HealthCheckService();
        var builder = new HealthChecksBuilder(new ServiceCollection(), service);
        builder.AddCheck("ping", () => HealthCheckResult.Healthy("pong"));

        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.True(report.Entries.ContainsKey("ping"));
    }
}

internal sealed class LambdaHealthCheck(Func<HealthCheckContext, Task<HealthCheckResult>> fn) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct) => fn(context);
}
