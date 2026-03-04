using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;

namespace CosmoApiServerTemplate.Controllers;

[Route("hello")]
public class HelloController : ControllerBase
{
    [HttpGet]
    public object Get()
    {
        return new { message = "Hello from CosmoApiServer Controller!" };
    }

    [HttpGet("{name}")]
    public object Greet(string name)
    {
        return new { message = $"Hello, {name}!" };
    }
}