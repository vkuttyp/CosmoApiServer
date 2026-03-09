using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

/// <summary>
/// Minimal middleware to serve a pre-generated OpenAPI document.
/// </summary>
public sealed class OpenApiMiddleware(string path, object spec) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Method == CosmoApiServer.Core.Http.HttpMethod.GET && context.Request.Path == path)
        {
            context.Response.StatusCode = 200;
            context.Response.WriteJson(spec);
            return;
        }

        await next(context);
    }
}
