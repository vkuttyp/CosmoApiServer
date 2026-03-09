using System.Reflection;
using System.Text;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Controllers;

public class ComponentScannerTests
{
    [Route("/test-route/{Id}")]
    private sealed class RoutableComponent : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter] public string? Id { get; set; }
        [Microsoft.AspNetCore.Components.Parameter] public string? QueryParam { get; set; }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.AddContent(0, $"Id:{Id}, Query:{QueryParam}");
        }
    }

    [Fact]
    public async Task RegisterComponents_RegistersRouteAndBindsParams()
    {
        var routeTable = new RouteTable();
        var services = new ServiceCollection().BuildServiceProvider();
        var assembly = Assembly.GetExecutingAssembly();

        ComponentScanner.RegisterComponents(new[] { assembly }, routeTable, services);

        var match = routeTable.Match(HttpMethod.GET, "/test-route/123");
        Assert.NotNull(match);

        // Simulate a request
        var request = new HttpRequest 
        { 
            Method = HttpMethod.GET, 
            Path = "/test-route/123",
            RouteValues = new Dictionary<string, string> { ["Id"] = "123" },
            Query = new Dictionary<string, string> { ["QueryParam"] = "abc" }
        };
        var response = new HttpResponse();
        var context = new HttpContext(request, response, services);

        await match.Entry.Handler(context);

        // Check rendered output
        var body = Encoding.UTF8.GetString(response.Body);
        Assert.Contains("Id:123, Query:abc", body);
    }

    [Route("/validated-route")]
    private sealed class ValidatedRoutableComponent : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter]
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Missing param")]
        public string? MyParam { get; set; }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            if (ModelState.TryGetValue("MyParam", out var msg))
                builder.AddContent(0, msg);
        }
    }

    [Fact]
    public async Task RegisterComponents_AutomaticallyValidatesRoutableComponents()
    {
        var routeTable = new RouteTable();
        var services = new ServiceCollection().BuildServiceProvider();
        var assembly = Assembly.GetExecutingAssembly();

        ComponentScanner.RegisterComponents(new[] { assembly }, routeTable, services);

        var match = routeTable.Match(HttpMethod.GET, "/validated-route");
        Assert.NotNull(match);

        // Request WITHOUT the required query parameter
        var request = new HttpRequest 
        { 
            Method = HttpMethod.GET, 
            Path = "/validated-route",
            Query = new Dictionary<string, string>() 
        };
        var response = new HttpResponse();
        var context = new HttpContext(request, response, services);

        await match.Entry.Handler(context);

        var body = Encoding.UTF8.GetString(response.Body);
        Assert.Contains("Missing param", body);
    }

    [Fact]
    public void RegisterComponents_DetectsAppAndLayout()
    {
        // Reset scanner state for clean test
        ComponentScanner._appType = null;
        ComponentScanner._mainLayoutType = null;

        var routeTable = new RouteTable();
        var services = new ServiceCollection().BuildServiceProvider();
        
        ComponentScanner.RegisterComponents(new[] { Assembly.GetExecutingAssembly() }, routeTable, services);
        
        // No App/MainLayout in this assembly, so they should remain null
        Assert.Null(ComponentScanner._appType);
        Assert.Null(ComponentScanner._mainLayoutType);
    }
}
