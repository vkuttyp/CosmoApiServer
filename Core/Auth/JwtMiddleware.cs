using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CosmoApiServer.Core.Auth;

/// <summary>
/// Reads the "Authorization: Bearer {token}" header, validates it via JwtService,
/// and sets HttpContext.User on success. Non-blocking — 401s are enforced by [Authorize].
/// </summary>
public sealed class JwtMiddleware : Middleware.IMiddleware
{
    public ValueTask InvokeAsync(HttpContext context, Middleware.RequestDelegate next)
    {
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim().Trim('"');
            var jwtService = context.RequestServices.GetService<JwtService>();
            if (jwtService is not null)
            {
                context.User = jwtService.ValidateToken(token);
                if (context.User is null)
                {
                    var logger = context.RequestServices.GetService<ILogger<JwtMiddleware>>();
                    logger?.LogWarning("JWT token validation failed for request to {Path}. Ensure the token is a valid Base64Url string and matches the Secret/Issuer/Audience.", context.Request.Path);
                }
            }
        }

        return next(context);
    }
}
