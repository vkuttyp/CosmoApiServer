using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class RequestTimeoutOptions
{
    /// <summary>Default timeout applied to all requests. Zero or negative means no timeout.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class RequestTimeoutMiddleware(RequestTimeoutOptions options) : IMiddleware
{
    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (options.DefaultTimeout <= TimeSpan.Zero)
        {
            await next(context);
            return;
        }

        var originalAborted = context.RequestAborted;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(originalAborted);
        cts.CancelAfter(options.DefaultTimeout);
        context.RequestAborted = cts.Token;

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !originalAborted.IsCancellationRequested)
        {
            // Our timeout fired (not the client disconnecting)
            context.Response.StatusCode = 504;
            try { context.Response.WriteText("Request timed out."); } catch { }
        }
    }
}
