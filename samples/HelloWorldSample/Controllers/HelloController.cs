using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;

namespace HelloWorldSample.Controllers;

[Route("hello")]
public class HelloController : ControllerBase
{
    [HttpGet]
    public IActionResult GetHello() =>
        Ok(new { message = "Hello from CosmoApiServer!", timestamp = DateTime.UtcNow });

    [HttpGet("{name}")]
    public IActionResult GetHelloName(string name) =>
        Ok(new { message = $"Hello, {name}!", timestamp = DateTime.UtcNow });

    [HttpPost]
    public IActionResult PostHello([FromBody] GreetingRequest req) =>
        req is null || string.IsNullOrWhiteSpace(req.Name)
            ? BadRequest("Name is required")
            : Created($"/hello/{req.Name}", new { message = $"Hello, {req.Name}!", created = true });
}

public record GreetingRequest(string Name);
