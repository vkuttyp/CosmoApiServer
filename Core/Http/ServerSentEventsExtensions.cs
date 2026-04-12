using System.Text;

namespace CosmoApiServer.Core.Http;

/// <summary>
/// Server-Sent Events (SSE) helpers for <see cref="HttpResponse"/>.
///
/// SSE is the standard mechanism for pushing real-time updates from server to browser
/// (live dashboards, notification feeds, progress streams) without polling or WebSockets.
///
/// Usage:
/// <code>
/// app.MapGet("/api/events", async ctx =>
/// {
///     await ctx.Response.BeginSseAsync(ctx.RequestAborted);
///     await foreach (var item in GetUpdatesAsync(ctx.RequestAborted))
///         await ctx.Response.WriteSseAsync(item, eventName: "update", cancellationToken: ctx.RequestAborted);
/// });
/// </code>
/// </summary>
public static class ServerSentEventsExtensions
{
    /// <summary>
    /// Sets the required SSE response headers and writes the HTTP 200 status.
    /// Must be called before any <see cref="WriteSseAsync"/> calls.
    /// </summary>
    public static ValueTask BeginSseAsync(this HttpResponse response, CancellationToken cancellationToken = default)
    {
        response.StatusCode = 200;
        response.Headers["Content-Type"]  = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache, no-store";
        response.Headers["Connection"]    = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no"; // disable nginx response buffering

        // Kick off the chunked response so the first WriteSseAsync doesn't stall.
        // Writing an empty comment line also serves as an initial heartbeat.
        return WriteSseCommentAsync(response, "stream-open", cancellationToken);
    }

    /// <summary>
    /// Writes one SSE event to the response stream.
    /// </summary>
    /// <param name="response">The HTTP response.</param>
    /// <param name="data">The event payload (typically JSON). Multi-line strings are split into multiple data: lines.</param>
    /// <param name="eventName">Optional event type (sent as <c>event: name</c>). Clients filter by type with <c>addEventListener</c>.</param>
    /// <param name="id">Optional event ID for reconnect tracking.</param>
    /// <param name="retry">Optional reconnect delay hint (milliseconds).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static ValueTask WriteSseAsync(
        this HttpResponse response,
        string data,
        string? eventName = null,
        string? id = null,
        int? retry = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        if (eventName is not null)
            sb.Append("event: ").Append(eventName).Append('\n');

        if (id is not null)
            sb.Append("id: ").Append(id).Append('\n');

        if (retry is not null)
            sb.Append("retry: ").Append(retry.Value).Append('\n');

        // SSE spec: each line of multi-line data gets its own "data:" prefix.
        foreach (var line in data.Split('\n'))
            sb.Append("data: ").Append(line.TrimEnd('\r')).Append('\n');

        // Events are separated by a blank line.
        sb.Append('\n');

        response.Write(Encoding.UTF8.GetBytes(sb.ToString()));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Writes an SSE comment line (ignored by clients, useful for keepalive pings).
    /// </summary>
    public static ValueTask WriteSseCommentAsync(
        this HttpResponse response,
        string comment = "ping",
        CancellationToken cancellationToken = default)
    {
        response.Write(Encoding.UTF8.GetBytes($": {comment}\n\n"));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sends periodic SSE heartbeat comments on the given interval.
    /// Run this concurrently with your event-producing loop to keep the connection alive
    /// through proxies and load balancers that close idle connections.
    /// </summary>
    public static async Task SendSseHeartbeatsAsync(
        this HttpResponse response,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
                await WriteSseCommentAsync(response, "heartbeat", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
