using System.Text.Json;
using System.Text.Json.Serialization;
using CosmoApiServer.Core.HealthChecks;
using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Middleware;

public sealed class HealthCheckMiddleware(string path) : IMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var service = context.RequestServices.GetService<HealthCheckService>();
        if (service is null)
        {
            context.Response.StatusCode = 503;
            context.Response.WriteText("Health check service not registered.");
            return;
        }

        var report = await service.RunAsync(context.RequestAborted);

        context.Response.StatusCode = report.Status == HealthStatus.Unhealthy ? 503 : 200;
        context.Response.Headers["Content-Type"] = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.ToString("c"),
            entries = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)new
                {
                    status = kvp.Value.Status.ToString(),
                    duration = kvp.Value.Duration.ToString("c"),
                    description = kvp.Value.Description,
                    data = kvp.Value.Data
                })
        };

        context.Response.WriteJson(payload);
    }
}
