using System.ComponentModel.DataAnnotations;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;
using CosmoApiServer.Core.Controllers.Filters;

namespace FeatureShowcase.Controllers;

public class LogFilter : ActionFilterAttribute
{
    public override Task OnActionExecutingAsync(ActionExecutingContext context)
    {
        Console.WriteLine($"[Filter] Executing action on {context.HttpContext.Request.Path}");
        return Task.CompletedTask;
    }
}

public class ProductQuery
{
    [Required]
    public string Category { get; set; } = "";
    
    [Range(1, 100)]
    public int Page { get; set; } = 1;
}

[Route("api/showcase")]
[LogFilter]
public class ShowcaseController : ControllerBase
{
    // 1. Complex Query Binding + Validation
    [HttpGet("products")]
    public IActionResult GetProducts([FromQuery] ProductQuery query)
    {
        if (!TryValidate(query))
            return StatusCode(400, ModelState);

        return Ok(new { message = "Products filtered", query });
    }

    // 2. FromHeader and FromServices
    [HttpGet("info")]
    public IActionResult GetInfo(
        [FromHeader("User-Agent")] string agent,
        [FromServices] IMyService service)
    {
        return Ok(new { 
            userAgent = agent, 
            serviceData = service.GetData() 
        });
    }

    // 3. Exception Handling demonstration
    [HttpGet("error")]
    public IActionResult ThrowError()
    {
        throw new Exception("This is a planned demonstration error!");
    }

    // 4. File Result
    [HttpGet("download")]
    public IActionResult Download()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("Hello from Cosmo File Result!");
        return File(content, "text/plain", "hello.txt");
    }
}
