using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace CosmoApiServer.Core.Tests.Controllers;

public class ModelBindingTests
{
    [Theory]
    [InlineData("123", typeof(int), 123)]
    [InlineData("123", typeof(int?), 123)]
    [InlineData("", typeof(int?), null)]
    [InlineData("true", typeof(bool), true)]
    [InlineData("2026-03-05T00:00:00Z", typeof(string), "2026-03-05T00:00:00Z")]
    public void Convert_HandlesTypesCorrectly(string value, Type targetType, object? expected)
    {
        // Use reflection to access the private static Convert method in ControllerScanner
        var method = typeof(ControllerScanner).GetMethod("Convert", BindingFlags.NonPublic | BindingFlags.Static);
        var result = method!.Invoke(null, [value, targetType]);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_HandlesGuid()
    {
        var guid = Guid.NewGuid();
        var method = typeof(ControllerScanner).GetMethod("Convert", BindingFlags.NonPublic | BindingFlags.Static);
        var result = method!.Invoke(null, [guid.ToString(), typeof(Guid)]);

        Assert.Equal(guid, result);
    }

    public class TestQueryModel
    {
        public int Page { get; set; }
        public string? Filter { get; set; }
    }

    [Fact]
    public void CompileComplexQueryBinder_BindsPropertiesCorrectly()
    {
        var method = typeof(ControllerScanner).GetMethod("CompileComplexQueryBinder", BindingFlags.NonPublic | BindingFlags.Static);
        var binder = (Func<IReadOnlyDictionary<string, string>, object?>)method!.Invoke(null, [typeof(TestQueryModel), ""])!;

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "page", "5" },
            { "filter", "active" }
        };

        var result = binder(query) as TestQueryModel;

        Assert.NotNull(result);
        Assert.Equal(5, result.Page);
        Assert.Equal("active", result.Filter);
    }

    [Fact]
    public void CompileComplexQueryBinder_BindsPropertiesWithPrefixCorrectly()
    {
        var method = typeof(ControllerScanner).GetMethod("CompileComplexQueryBinder", BindingFlags.NonPublic | BindingFlags.Static);
        var binder = (Func<IReadOnlyDictionary<string, string>, object?>)method!.Invoke(null, [typeof(TestQueryModel), "filter"])!;

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "filter.page", "10" },
            { "filter.filter", "pending" }
        };

        var result = binder(query) as TestQueryModel;

        Assert.NotNull(result);
        Assert.Equal(10, result.Page);
        Assert.Equal("pending", result.Filter);
    }
}

