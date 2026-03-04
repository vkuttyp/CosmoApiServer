using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Controllers;

public interface IActionResult
{
    Task ExecuteAsync(HttpResponse response);
}

/// <summary>
/// Base class for all controllers. Provides action result factory methods.
/// </summary>
public abstract class ControllerBase
{
    public HttpContext HttpContext { get; internal set; } = null!;
    public HttpRequest Request => HttpContext.Request;
    public HttpResponse Response => HttpContext.Response;

    protected IActionResult Ok() => new StatusCodeResult(200);
    protected IActionResult Ok<T>(T value) => new JsonResult<T>(value, 200);
    protected IActionResult Created<T>(string location, T value) => new CreatedResult<T>(location, value);
    protected IActionResult NoContent() => new StatusCodeResult(204);
    protected IActionResult NotFound() => new StatusCodeResult(404);
    protected IActionResult NotFound(string message) => new TextResult(404, message);
    protected IActionResult BadRequest() => new StatusCodeResult(400);
    protected IActionResult BadRequest(string message) => new TextResult(400, message);
    protected IActionResult StatusCode(int code) => new StatusCodeResult(code);
    protected IActionResult StatusCode(int code, object value) => new JsonResult<object>(value, code);
}

// ── Built-in IActionResult implementations ─────────────────────────────────

public sealed class StatusCodeResult(int statusCode) : IActionResult
{
    public Task ExecuteAsync(HttpResponse response)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}

public sealed class TextResult(int statusCode, string message) : IActionResult
{
    public Task ExecuteAsync(HttpResponse response)
    {
        response.StatusCode = statusCode;
        response.WriteText(message);
        return Task.CompletedTask;
    }
}

public sealed class JsonResult<T>(T value, int statusCode = 200) : IActionResult
{
    public Task ExecuteAsync(HttpResponse response)
    {
        response.StatusCode = statusCode;
        response.WriteJson(value);
        return Task.CompletedTask;
    }
}

public sealed class CreatedResult<T>(string location, T value) : IActionResult
{
    public Task ExecuteAsync(HttpResponse response)
    {
        response.StatusCode = 201;
        response.Headers["Location"] = location;
        response.WriteJson(value);
        return Task.CompletedTask;
    }
}
