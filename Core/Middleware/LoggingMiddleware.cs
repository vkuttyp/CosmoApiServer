using System.Diagnostics;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class LoggingMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sw = Stopwatch.StartNew();
        string qs = string.IsNullOrEmpty(context.Request.QueryString) ? "" : $"?{context.Request.QueryString}";
        Console.WriteLine($"  --> {context.Request.Method} {context.Request.Path}{qs}");
        await next(context);
        sw.Stop();
        Console.WriteLine($"  <-- {context.Response.StatusCode} ({sw.ElapsedMilliseconds}ms)");
    }
}
