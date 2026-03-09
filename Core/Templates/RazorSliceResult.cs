using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Templates;

/// <summary>
/// An IActionResult that renders a Razor Slice to the HTTP response.
/// </summary>
public sealed class RazorSliceResult(RazorSlice slice, int statusCode = 200) : IActionResult
{
    public async Task ExecuteAsync(HttpResponse response)
    {
        response.StatusCode = statusCode;
        slice.HttpContext = response.HttpContext; // Assuming HttpResponse has a reference back to HttpContext
        await slice.RenderToResponseAsync(response);
    }
}
