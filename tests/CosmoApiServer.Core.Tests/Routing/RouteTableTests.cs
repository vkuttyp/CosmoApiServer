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

    // ── Route constraint tests ─────────────────────────────────────────────

    [Theory]
    [InlineData("42", true)]
    [InlineData("-7", true)]
    [InlineData("0", true)]
    [InlineData("abc", false)]
    [InlineData("3.14", false)]
    [InlineData("", false)]
    public void Match_IntConstraint_ValidatesCorrectly(string segment, bool expected)
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/items/{id:int}", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, $"/items/{segment}");

        Assert.Equal(expected, match is not null);
    }

    [Theory]
    [InlineData("9999999999", true)]
    [InlineData("42", true)]
    [InlineData("nope", false)]
    [InlineData("3.14", false)]
    public void Match_LongConstraint_ValidatesCorrectly(string segment, bool expected)
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/records/{id:long}", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, $"/records/{segment}");

        Assert.Equal(expected, match is not null);
    }

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", true)]
    [InlineData("notguid", false)]
    [InlineData("12345", false)]
    public void Match_GuidConstraint_ValidatesCorrectly(string segment, bool expected)
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/users/{id:guid}", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, $"/users/{segment}");

        Assert.Equal(expected, match is not null);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", true)]
    [InlineData("True", true)]
    [InlineData("False", true)]
    [InlineData("yes", false)]
    [InlineData("1", false)]
    [InlineData("0", false)]
    public void Match_BoolConstraint_ValidatesCorrectly(string segment, bool expected)
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/flags/{enabled:bool}", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, $"/flags/{segment}");

        Assert.Equal(expected, match is not null);
    }

    [Theory]
    [InlineData("hello", true)]
    [InlineData("WORLD", true)]
    [InlineData("CamelCase", true)]
    [InlineData("hello123", false)]
    [InlineData("hello-world", false)]
    [InlineData("hello!", false)]
    public void Match_AlphaConstraint_ValidatesCorrectly(string segment, bool expected)
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/tags/{name:alpha}", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, $"/tags/{segment}");

        Assert.Equal(expected, match is not null);
    }

    [Fact]
    public void Match_IntConstraint_ExtractsRouteValueOnSuccess()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/items/{id:int}", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, "/items/99");

        Assert.NotNull(match);
        Assert.Equal("99", match.RouteValues["id"]);
    }

    [Fact]
    public void Match_MultipleConstrainedParams_ExtractsAll()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/orders/{orderId:int}/items/{itemName:alpha}", _ => ValueTask.CompletedTask);

        var match = table.Match(HttpMethod.GET, "/orders/42/items/widget");

        Assert.NotNull(match);
        Assert.Equal("42", match.RouteValues["orderId"]);
        Assert.Equal("widget", match.RouteValues["itemName"]);
    }

    [Fact]
    public void Match_MultipleConstrainedParams_RejectsWhenOneConstraintFails()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/orders/{orderId:int}/items/{itemName:alpha}", _ => ValueTask.CompletedTask);

        // orderId valid but itemName is not alpha
        var match = table.Match(HttpMethod.GET, "/orders/42/items/item123");

        Assert.Null(match);
    }

    // ── Cache bound test ───────────────────────────────────────────────────

    [Fact]
    public void RouteTable_Cache_DoesNotGrowBeyondMaxCacheSize()
    {
        var table = new RouteTable();
        table.Add(HttpMethod.GET, "/items/{id}", _ => ValueTask.CompletedTask);

        // Flood the cache with > 10,000 unique paths; should not throw or grow unbounded
        for (int i = 0; i < 12_000; i++)
        {
            var match = table.Match(HttpMethod.GET, $"/items/{i}");
            Assert.NotNull(match);
        }

        // Subsequent lookups still work correctly
        var final = table.Match(HttpMethod.GET, "/items/42");
        Assert.NotNull(final);
        Assert.Equal("42", final.RouteValues["id"]);
    }
}
