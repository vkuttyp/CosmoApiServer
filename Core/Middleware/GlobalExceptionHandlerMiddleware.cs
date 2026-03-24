using System.Net;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

/// <summary>
/// Middleware that catches all unhandled exceptions and returns a JSON response.
/// </summary>
public class GlobalExceptionHandlerMiddleware : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private static ValueTask HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Log full details server-side only
        Console.Error.WriteLine($"[ERROR] {DateTime.UtcNow:O} {context.Request.Method} {context.Request.Path}");
        Console.Error.WriteLine(exception.ToString());

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        // Do NOT expose exception.Message to clients — it may contain sensitive info
        // (SQL queries, file paths, internal stack details)
        var errorResponse = new
        {
            message = "An unexpected error occurred.",
            status = 500
        };

        try
        {
            context.Response.WriteJson(errorResponse);
        }
        catch
        {
            // Response may have already started; swallow to avoid crashing the connection
        }
        return ValueTask.CompletedTask;
    }
}
