using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Templates;

/// <summary>
/// An IActionResult that renders a Razor Component to the HTTP response.
/// </summary>
public sealed class ComponentResult(ComponentBase component, int statusCode = 200) : IActionResult
{
    public async ValueTask ExecuteAsync(HttpResponse response)
    {
        response.StatusCode = statusCode;
        component.HttpContext = response.HttpContext;
        await component.RenderToResponseAsync(response);
    }
}
