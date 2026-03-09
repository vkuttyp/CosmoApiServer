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
        Console.WriteLine($"[ERROR] {DateTime.UtcNow:O} {context.Request.Method} {context.Request.Path}");
        Console.WriteLine(exception.ToString());

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        
        var errorResponse = new
        {
            message = "An unexpected error occurred.",
            detail = exception.Message,
            status = 500
        };

        context.Response.WriteJson(errorResponse);
        return ValueTask.CompletedTask;
    }
}
