using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public delegate Task RequestDelegate(HttpContext context);

public interface IMiddleware
{
    Task InvokeAsync(HttpContext context, RequestDelegate next);
}
