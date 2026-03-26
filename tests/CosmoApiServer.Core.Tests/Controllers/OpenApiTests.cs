using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Controllers.Attributes;

namespace CosmoApiServer.Core.Tests.Controllers;

public class OpenApiTests
{
    public class UserDto
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    [Route("api/test")]
    public class SampleController : ControllerBase
    {
        [HttpGet("{id}")]
        public string GetById([FromRoute] int id, [FromQuery] string filter) => "ok";

        [HttpPost("")]
        public void Create([FromBody] UserDto user) { }
    }

    [Fact]
    public void Generate_ProducesCorrectOpenApiSpec()
    {
        var info = new OpenApiInfo { Title = "Test API", Version = "v1" };
        var spec = OpenApiGenerator.Generate([typeof(SampleController)], info);

        Assert.Equal("3.0.0", spec["openapi"]);
        var specInfo = (OpenApiInfo)spec["info"];
        Assert.Equal("Test API", specInfo.Title);

        var paths = (Dictionary<string, object>)spec["paths"];
        Assert.True(paths.ContainsKey("/api/test/{id}"));
        Assert.True(paths.ContainsKey("/api/test"));

        var getOp = (Dictionary<string, object>)((Dictionary<string, object>)paths["/api/test/{id}"])["get"];
        var parameters = (List<object>)getOp["parameters"];
        Assert.Equal(2, parameters.Count);
        
        var idParam = (Dictionary<string, object>)parameters.First(p => ((Dictionary<string, object>)p)["name"].ToString() == "id");
        Assert.Equal("path", idParam["in"]);
        
        var postOp = (Dictionary<string, object>)((Dictionary<string, object>)paths["/api/test"])["post"];
        Assert.True(postOp.ContainsKey("requestBody"));

        var components = (Dictionary<string, object>)spec["components"];
        var schemas = (Dictionary<string, object>)components["schemas"];
        Assert.True(schemas.ContainsKey("UserDto"));
        
        var userSchema = (Dictionary<string, object>)schemas["UserDto"];
        var properties = (Dictionary<string, object>)userSchema["properties"];
        Assert.True(properties.ContainsKey("Name"));
        Assert.True(properties.ContainsKey("Age"));
    }
}
