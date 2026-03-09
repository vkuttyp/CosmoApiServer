using CosmoApiServer.Core.Routing;
using CosmoApiServer.Core.Http;
using HttpMethod = CosmoApiServer.Core.Http.HttpMethod;

namespace CosmoApiServer.Core.Tests.Routing;

public class RouteTableTests
{
    [Fact]
    public void Match_StaticPath_ReturnsMatch()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/ping", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, "/ping");

        Assert.NotNull(match);
        Assert.Empty(match.RouteValues);
    }

    [Fact]
    public void Match_PathWithParam_ExtractsRouteValue()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/users/{id}", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, "/users/42");

        Assert.NotNull(match);
        Assert.Equal("42", match.RouteValues["id"]);
    }

    [Fact]
    public void Match_MultipleParams_ExtractsAll()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/orders/{orderId}/items/{itemId}", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, "/orders/99/items/7");

        Assert.NotNull(match);
        Assert.Equal("99", match.RouteValues["orderId"]);
        Assert.Equal("7", match.RouteValues["itemId"]);
    }

    [Fact]
    public void Match_WrongMethod_ReturnsNull()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/ping", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.POST, "/ping");

        Assert.Null(match);
    }

    [Fact]
    public void Match_UnknownPath_ReturnsNull()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/ping", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, "/notfound");

        Assert.Null(match);
    }

    [Fact]
    public void Match_PathWithQueryString_StillMatches()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/search", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, "/search?q=hello");

        Assert.NotNull(match);
    }

    [Fact]
    public void Match_CaseInsensitivePath_Matches()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/Hello", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, "/hello");

        Assert.NotNull(match);
    }
}
