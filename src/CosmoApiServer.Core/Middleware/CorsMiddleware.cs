using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = ["*"];
    public string[] AllowedMethods { get; set; } = ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"];
    public string[] AllowedHeaders { get; set; } = ["Content-Type", "Authorization"];
}

public sealed class CorsMiddleware(CorsOptions options) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var origin = context.Request.Headers.TryGetValue("Origin", out var o) ? o : "*";
        var allowed = options.AllowedOrigins.Contains("*") || options.AllowedOrigins.Contains(origin);

        if (allowed)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Methods"] = string.Join(", ", options.AllowedMethods);
            context.Response.Headers["Access-Control-Allow-Headers"] = string.Join(", ", options.AllowedHeaders);
        }

        // Handle pre-flight
        if (context.Request.Method == Http.HttpMethod.OPTIONS)
        {
            context.Response.StatusCode = 204;
            return;
        }

        await next(context);
    }
}
