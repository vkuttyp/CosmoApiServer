using System.Text.Json;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;

namespace CosmoApiServer.Core.Routing;

/// <summary>
/// Factory methods for common HTTP result types, analogous to ASP.NET Core's TypedResults.
/// Each method returns a <see cref="RequestDelegate"/> that writes the appropriate response.
/// Use the returned delegate directly as a minimal-API handler or return it from a lambda.
/// </summary>
public static class TypedResults
{
    // ── 2xx ──────────────────────────────────────────────────────────────────

    public static RequestDelegate Ok(object? value = null) => ctx =>
    {
        ctx.Response.StatusCode = 200;
        if (value is not null) ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Ok<T>(T value) => ctx =>
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Created(string? location, object? value = null) => ctx =>
    {
        ctx.Response.StatusCode = 201;
        if (location is not null) ctx.Response.Headers["Location"] = location;
        if (value is not null) ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Created<T>(string? location, T value) => ctx =>
    {
        ctx.Response.StatusCode = 201;
        if (location is not null) ctx.Response.Headers["Location"] = location;
        ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Accepted(string? location = null, object? value = null) => ctx =>
    {
        ctx.Response.StatusCode = 202;
        if (location is not null) ctx.Response.Headers["Location"] = location;
        if (value is not null) ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate NoContent() => ctx =>
    {
        ctx.Response.StatusCode = 204;
        return ValueTask.CompletedTask;
    };

    // ── 3xx ──────────────────────────────────────────────────────────────────

    public static RequestDelegate Redirect(string url, bool permanent = false) => ctx =>
    {
        ctx.Response.StatusCode = permanent ? 301 : 302;
        ctx.Response.Headers["Location"] = url;
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate RedirectPermanent(string url) => Redirect(url, permanent: true);

    // ── 4xx ──────────────────────────────────────────────────────────────────

    public static RequestDelegate BadRequest(object? error = null) => ctx =>
    {
        ctx.Response.StatusCode = 400;
        if (error is string s) ctx.Response.WriteText(s);
        else if (error is not null) ctx.Response.WriteJson(error);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Unauthorized() => ctx =>
    {
        ctx.Response.StatusCode = 401;
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Forbid() => ctx =>
    {
        ctx.Response.StatusCode = 403;
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate NotFound(object? value = null) => ctx =>
    {
        ctx.Response.StatusCode = 404;
        if (value is not null) ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Conflict(object? value = null) => ctx =>
    {
        ctx.Response.StatusCode = 409;
        if (value is not null) ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate UnprocessableEntity(object? value = null) => ctx =>
    {
        ctx.Response.StatusCode = 422;
        if (value is not null) ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate TooManyRequests() => ctx =>
    {
        ctx.Response.StatusCode = 429;
        return ValueTask.CompletedTask;
    };

    // ── 5xx ──────────────────────────────────────────────────────────────────

    public static RequestDelegate InternalServerError(object? value = null) => ctx =>
    {
        ctx.Response.StatusCode = 500;
        if (value is not null) ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    // ── Content ───────────────────────────────────────────────────────────────

    public static RequestDelegate Text(string text, int statusCode = 200) => ctx =>
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.WriteText(text);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Json<T>(T value, int statusCode = 200) => ctx =>
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.WriteJson(value);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Bytes(byte[] data, string contentType = "application/octet-stream", int statusCode = 200) => ctx =>
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.Headers["Content-Type"] = contentType;
        ctx.Response.Write(data);
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Stream(Func<System.IO.Stream, Task> writer, string contentType = "application/octet-stream") => ctx =>
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.Headers["Content-Type"] = contentType;
        ctx.Response.Headers["Transfer-Encoding"] = "chunked";
        ctx.StreamingBodyWriter = writer;
        return ValueTask.CompletedTask;
    };

    public static RequestDelegate Problem(string? title = null, string? detail = null, int statusCode = 500, string? type = null) => ctx =>
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.Headers["Content-Type"] = "application/problem+json";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = type ?? "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title = title ?? "An error occurred",
            status = statusCode,
            detail
        });
        ctx.Response.Write(bytes);
        return ValueTask.CompletedTask;
    };
}
