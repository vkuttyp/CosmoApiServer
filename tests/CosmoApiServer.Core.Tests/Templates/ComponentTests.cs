using System.Text;
using System.Reflection;
using CosmoApiServer.Core.Templates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace CosmoApiServer.Core.Tests.Templates;

public class ComponentTests
{
    private sealed class SimpleComponent : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter] public string? Title { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "container");
            builder.OpenElement(2, "h1");
            builder.AddContent(3, Title);
            builder.CloseElement();
            builder.OpenElement(4, "p");
            builder.AddContent(5, "Hello World");
            builder.CloseElement();
            builder.CloseElement();
        }
    }

    private sealed class ChildComponent : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter] public string? Message { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddContent(1, Message);
            builder.CloseElement();
        }
    }

    private sealed class ParentComponent : Microsoft.AspNetCore.Components.ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.OpenComponent<ChildComponent>(1);
            builder.AddComponentParameter(2, "Message", "Nested Content");
            builder.CloseComponent();
            builder.CloseElement();
        }
    }

    [Fact]
    public async Task RenderAsync_ProducesCorrectHtml()
    {
        var component = new SimpleComponent { Title = "Test Title" };
        var html = await component.RenderAsync();

        Assert.Equal("<div class=\"container\"><h1>Test Title</h1><p>Hello World</p></div>", html);
    }

    [Fact]
    public async Task RenderAsync_HandlesNesting()
    {
        var component = new ParentComponent();
        var html = await component.RenderAsync();

        Assert.Equal("<div><span>Nested Content</span></div>", html);
    }

    [Fact]
    public async Task RenderAsync_EncodesContent()
    {
        var component = new SimpleComponent { Title = "<script>alert('xss')</script>" };
        var html = await component.RenderAsync();

        // WebUtility.HtmlEncode encodes < > and '
        Assert.Contains("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;", html);
    }

    private sealed class ValidatedComponent : Microsoft.AspNetCore.Components.ComponentBase
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string? Name { get; set; }

        public bool PerformValidation() => TryValidate(this);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<ValidationChild>(0);
            builder.CloseComponent();
        }
    }

    private sealed class ValidationChild : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.CascadingParameter]
        public Dictionary<string, string>? Errors { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Errors != null && Errors.TryGetValue("Name", out var msg))
            {
                builder.AddContent(0, msg);
            }
        }
    }

    [Fact]
    public void TryValidate_PopulatesModelState()
    {
        var component = new ValidatedComponent { Name = null };
        var isValid = component.PerformValidation();

        Assert.False(isValid);
        Assert.True(component.ModelState.ContainsKey("Name"));
    }

    [Fact]
    public async Task RenderAsync_CascadesModelStateToChildren()
    {
        var component = new ValidatedComponent { Name = null };
        component.PerformValidation(); // Populate errors

        var html = await component.RenderAsync();

        // The error message comes from the child component using cascading ModelState
        Assert.Contains("The Name field is required", html);
    }

    [Fact]
    public async Task RenderAsync_HandlesBooleanAttributes()
    {
        var component = new BooleanAttrComponent();
        var html = await component.RenderAsync();

        // Current implementation uses full closing tags for simplicity in the stub
        Assert.Contains("<input checked></input>", html);
        Assert.Contains("<input></input>", html);
    }

    private sealed class BooleanAttrComponent : Microsoft.AspNetCore.Components.ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "checked", true);
            builder.CloseElement();

            builder.OpenElement(2, "input");
            builder.AddAttribute(3, "unchecked", false);
            builder.CloseElement();
        }
    }
}
