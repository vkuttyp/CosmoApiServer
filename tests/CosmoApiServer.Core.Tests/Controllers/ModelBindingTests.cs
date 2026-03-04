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
}
