using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

/// <summary>
/// Adapts an inline <see cref="Func{HttpContext, RequestDelegate, ValueTask}"/>
/// to the <see cref="IMiddleware"/> interface. Lets callers register lambda
/// middleware via <c>builder.Use(...)</c> without defining a class for one-off
/// pipeline steps.
/// </summary>
public sealed class InlineMiddleware : IMiddleware
{
    private readonly Func<HttpContext, RequestDelegate, ValueTask> _handler;

    public InlineMiddleware(Func<HttpContext, RequestDelegate, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public ValueTask InvokeAsync(HttpContext context, RequestDelegate next) => _handler(context, next);
}
