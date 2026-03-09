using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public delegate ValueTask RequestDelegate(HttpContext context);

public interface IMiddleware
{
    ValueTask InvokeAsync(HttpContext context, RequestDelegate next);
}
