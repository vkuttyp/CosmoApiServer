using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

/// <summary>
/// Defines a structured exception handler, analogous to ASP.NET Core's IExceptionHandler.
/// Register implementations with <c>builder.AddExceptionHandler&lt;T&gt;()</c>.
/// Handlers are tried in registration order; the first to return true short-circuits.
/// </summary>
public interface IExceptionHandler
{
    /// <summary>
    /// Attempt to handle the exception. Return true to stop processing further handlers.
    /// </summary>
    ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct);
}
