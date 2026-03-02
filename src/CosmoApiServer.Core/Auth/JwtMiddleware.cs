using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Auth;

/// <summary>
/// Reads the "Authorization: Bearer {token}" header, validates it via JwtService,
/// and sets HttpContext.User on success. Non-blocking — 401s are enforced by [Authorize].
/// </summary>
public sealed class JwtMiddleware : Middleware.IMiddleware
{
    public Task InvokeAsync(HttpContext context, Middleware.RequestDelegate next)
    {
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            var jwtService = context.RequestServices.GetService<JwtService>();
            if (jwtService is not null)
                context.User = jwtService.ValidateToken(token);
        }

        return next(context);
    }
}
