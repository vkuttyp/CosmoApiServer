using System.Diagnostics;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class TracingOptions
{
    /// <summary>ActivitySource name reported in traces. Default: "CosmoApiServer".</summary>
    public string ServiceName { get; set; } = "CosmoApiServer";

    /// <summary>Whether to propagate W3C traceparent/tracestate headers from incoming requests.</summary>
    public bool PropagateIncomingContext { get; set; } = true;

    /// <summary>Record request/response attributes (method, path, status) on the span.</summary>
    public bool RecordHttpAttributes { get; set; } = true;
}

/// <summary>
/// Creates a System.Diagnostics.Activity (OpenTelemetry-compatible span) for each request.
/// Propagates W3C traceparent / tracestate from incoming headers.
/// Compatible with any OpenTelemetry SDK exporter (Jaeger, Zipkin, OTLP, etc.)
/// via AddSource("CosmoApiServer") in the OpenTelemetry SDK setup.
/// </summary>
public sealed class TracingMiddleware : IMiddleware
{
    private readonly TracingOptions _options;
    private readonly ActivitySource _activitySource;

    /// <summary>Exposed so callers can listen: ActivitySource.AddActivityListener(...).</summary>
    public ActivitySource ActivitySource => _activitySource;

    public TracingMiddleware(TracingOptions options)
    {
        _options = options;
        _activitySource = new ActivitySource(options.ServiceName);
    }

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ActivityContext parentContext = default;
        if (_options.PropagateIncomingContext &&
            context.Request.Headers.TryGetValue("traceparent", out var traceparent))
        {
            parentContext = ExtractW3C(traceparent,
                context.Request.Headers.TryGetValue("tracestate", out var ts) ? ts : null);
        }

        using var activity = _activitySource.StartActivity(
            $"{context.Request.Method} {context.Request.Path}",
            ActivityKind.Server,
            parentContext);

        if (activity is not null && _options.RecordHttpAttributes)
        {
            activity.SetTag("http.method", context.Request.Method.ToString());
            activity.SetTag("http.target", context.Request.Path);
            activity.SetTag("http.scheme",
                context.Items.TryGetValue("X-Forwarded-Proto", out var proto) ? proto?.ToString() : "http");
            if (context.Request.Headers.TryGetValue("User-Agent", out var ua))
                activity.SetTag("http.user_agent", ua);

            if (activity.Id is not null)
                context.Response.Headers["X-Trace-Id"] = activity.TraceId.ToString();
        }

        try
        {
            await next(context);

            if (activity is not null && _options.RecordHttpAttributes)
                activity.SetTag("http.status_code", context.Response.StatusCode);
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.SetTag("exception.type", ex.GetType().FullName);
                activity.SetTag("exception.message", ex.Message);
            }
            throw;
        }
    }

    /// <summary>Parses W3C traceparent: 00-{traceId}-{spanId}-{flags}</summary>
    private static ActivityContext ExtractW3C(string traceparent, string? tracestate)
    {
        try
        {
            var parts = traceparent.Split('-');
            if (parts.Length < 4) return default;
            var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
            var spanId  = ActivitySpanId.CreateFromString(parts[2].AsSpan());
            var flags   = byte.TryParse(parts[3], System.Globalization.NumberStyles.HexNumber, null, out var f)
                ? (ActivityTraceFlags)f : ActivityTraceFlags.None;
            return new ActivityContext(traceId, spanId, flags, tracestate, isRemote: true);
        }
        catch
        {
            return default;
        }
    }
}
