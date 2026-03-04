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

    /// <summary>
    /// Collection of validation errors for the current request.
    /// </summary>
    public Dictionary<string, string> ModelState { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether the model state is valid.
    /// </summary>
    public bool IsValid => ModelState.Count == 0;

    /// <summary>
    /// Validates the given model using DataAnnotations.
    /// Errors are added to <see cref="ModelState"/>.
    /// </summary>
    protected bool TryValidate(object? model)
    {
        if (model is null) return true;
        return ModelValidator.Validate(model, ModelState);
    }

    protected IActionResult Ok() => new StatusCodeResult(200);
    protected IActionResult Ok<T>(T value) => new JsonResult<T>(value, 200);
    protected IActionResult Created<T>(string location, T value) => new CreatedResult<T>(location, value);
    protected IActionResult NoContent() => new StatusCodeResult(204);
    protected IActionResult NotFound() => new StatusCodeResult(404);
    protected IActionResult NotFound(string message) => new TextResult(404, message);
    protected IActionResult BadRequest() => new StatusCodeResult(400);
    protected IActionResult BadRequest(string message) => new TextResult(400, message);
    protected IActionResult Unauthorized() => new StatusCodeResult(401);
    protected IActionResult Forbid() => new StatusCodeResult(403);
    protected IActionResult Conflict() => new StatusCodeResult(409);
    protected IActionResult Redirect(string url) => new RedirectResult(url, 302);
    protected IActionResult RedirectPermanent(string url) => new RedirectResult(url, 301);
    protected IActionResult File(byte[] contents, string contentType, string? fileName = null) => new FileContentResult(contents, contentType, fileName);
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

public sealed class RedirectResult(string url, int statusCode = 302) : IActionResult
{
    public Task ExecuteAsync(HttpResponse response)
    {
        response.StatusCode = statusCode;
        response.Headers["Location"] = url;
        return Task.CompletedTask;
    }
}

public sealed class FileContentResult(byte[] contents, string contentType, string? fileName = null) : IActionResult
{
    public Task ExecuteAsync(HttpResponse response)
    {
        response.StatusCode = 200;
        response.Headers["Content-Type"] = contentType;
        if (!string.IsNullOrEmpty(fileName))
        {
            response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
        }
        response.Write(contents);
        return Task.CompletedTask;
    }
}
