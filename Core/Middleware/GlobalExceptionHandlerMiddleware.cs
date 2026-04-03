using System.Net;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.ProblemDetails;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Middleware;

/// <summary>
/// Middleware that catches all unhandled exceptions and returns a JSON error response.
/// Uses IProblemDetailsService (RFC 7807) when registered; falls back to a plain JSON body.
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

    private static async ValueTask HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Log full details server-side only
        Console.Error.WriteLine($"[ERROR] {DateTime.UtcNow:O} {context.Request.Method} {context.Request.Path}");
        Console.Error.WriteLine(exception.ToString());

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        try
        {
            // Try registered IExceptionHandler implementations first (in registration order)
            var handlers = context.RequestServices.GetServices<IExceptionHandler>();
            foreach (var handler in handlers)
            {
                if (await handler.TryHandleAsync(context, exception, context.RequestAborted))
                    return;
            }

            // Fall back to ProblemDetails or plain JSON
            var problemDetailsService = context.RequestServices.GetService<IProblemDetailsService>();
            if (problemDetailsService is not null)
            {
                await problemDetailsService.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    Exception = exception,
                    ProblemDetails = new ProblemDetails.ProblemDetails { Status = 500 }
                });
            }
            else
            {
                // Do NOT expose exception.Message to clients — it may contain sensitive info
                // (SQL queries, file paths, internal stack details)
                context.Response.WriteJson(new { message = "An unexpected error occurred.", status = 500 });
            }
        }
        catch
        {
            // Response may have already started; swallow to avoid crashing the connection
        }
    }
}
