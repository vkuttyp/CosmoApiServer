using System.Diagnostics;
using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CosmoApiServer.Core.Middleware;

public sealed class LoggingMiddleware : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var logger = context.RequestServices.GetService<ILogger<LoggingMiddleware>>();
        var sw = Stopwatch.StartNew();
        string qs = string.IsNullOrEmpty(context.Request.QueryString) ? "" : $"?{context.Request.QueryString}";
        
        if (logger != null)
        {
            logger.LogInformation("  --> {Method} {Path}{QueryString}", context.Request.Method, context.Request.Path, qs);
        }
        else
        {
            Console.WriteLine($"  --> {context.Request.Method} {context.Request.Path}{qs}");
        }

        await next(context);
        sw.Stop();
        
        if (logger != null)
        {
            logger.LogInformation("  <-- {StatusCode} ({Elapsed}ms)", context.Response.StatusCode, sw.ElapsedMilliseconds);
        }
        else
        {
            Console.WriteLine($"  <-- {context.Response.StatusCode} ({sw.ElapsedMilliseconds}ms)");
        }
    }
}
