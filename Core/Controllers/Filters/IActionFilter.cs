using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Controllers.Filters;

/// <summary>
/// Context for ActionExecuting. Allows short-circuiting by setting <see cref="Result"/>.
/// </summary>
public sealed class ActionExecutingContext(HttpContext httpContext)
{
    public HttpContext HttpContext { get; } = httpContext;
    public Dictionary<string, string> ModelState { get; internal set; } = null!;
    
    /// <summary>
    /// If set, the action method is NOT called and this result is executed instead.
    /// </summary>
    public IActionResult? Result { get; set; }
}

/// <summary>
/// Context for ActionExecuted. Contains the result (or exception) of the action.
/// </summary>
public sealed class ActionExecutedContext(HttpContext httpContext, IActionResult? result, Exception? exception)
{
    public HttpContext HttpContext { get; } = httpContext;
    public IActionResult? Result { get; set; } = result;
    public Exception? Exception { get; } = exception;
    public bool ExceptionHandled { get; set; }
}

/// <summary>
/// Filter that runs before and after an action method execution.
/// </summary>
public interface IActionFilter
{
    Task OnActionExecutingAsync(ActionExecutingContext context);
    Task OnActionExecutedAsync(ActionExecutedContext context);
}

/// <summary>
/// Base attribute for applying action filters.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public abstract class ActionFilterAttribute : Attribute, IActionFilter
{
    public virtual Task OnActionExecutingAsync(ActionExecutingContext context) => Task.CompletedTask;
    public virtual Task OnActionExecutedAsync(ActionExecutedContext context) => Task.CompletedTask;
}
