using System.ComponentModel.DataAnnotations;
using CosmoApiServer.Core.Controllers;

namespace CosmoApiServer.Core.Tests.Controllers;

public class ModelValidatorTests
{
    public class TestModel
    {
        [Required]
        public string Name { get; set; } = null!;

        [EmailAddress]
        public string Email { get; set; } = null!;

        [Range(1, 100)]
        public int Age { get; set; }
    }

    [Fact]
    public void Validate_ReturnsTrue_ForValidModel()
    {
        var model = new TestModel { Name = "John", Email = "john@example.com", Age = 25 };
        var state = new Dictionary<string, string>();

        var result = ModelValidator.Validate(model, state);

        Assert.True(result);
        Assert.Empty(state);
    }

    [Fact]
    public void Validate_ReturnsFalse_AndPopulatesErrors_ForInvalidModel()
    {
        var model = new TestModel { Name = "", Email = "invalid-email", Age = 150 };
        var state = new Dictionary<string, string>();

        var result = ModelValidator.Validate(model, state);

        Assert.False(result);
        Assert.True(state.ContainsKey("Name"));
        Assert.True(state.ContainsKey("Email"));
        Assert.True(state.ContainsKey("Age"));
    }
}
