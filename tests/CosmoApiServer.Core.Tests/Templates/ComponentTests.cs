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

    // ── New Feature Tests ─────────────────────────────────────────────────

    [Fact]
    public void EventCallback_InvokeAsync_ExecutesCallback()
    {
        var called = false;
        var cb = new EventCallback(() => { called = true; return ValueTask.CompletedTask; });
        cb.InvokeAsync().GetAwaiter().GetResult();
        Assert.True(called);
    }

    [Fact]
    public void EventCallback_HasDelegate_ReportsCorrectly()
    {
        var empty = EventCallback.Empty;
        Assert.False(empty.HasDelegate);

        var withAction = new EventCallback(() => { });
        Assert.True(withAction.HasDelegate);
    }

    [Fact]
    public void EventCallbackT_InvokeAsync_PassesValue()
    {
        string? received = null;
        var cb = new EventCallback<string>(v => { received = v; return ValueTask.CompletedTask; });
        cb.InvokeAsync("hello").GetAwaiter().GetResult();
        Assert.Equal("hello", received);
    }

    [Fact]
    public void EditContext_Validate_PopulatesMessages()
    {
        var model = new TestModel { Name = null };
        var ctx = new EditContext(model);

        var valid = ctx.Validate();

        Assert.False(valid);
        Assert.True(ctx.HasError("Name"));
        Assert.Contains("required", ctx.GetError("Name")!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EditContext_Validate_ValidModel_ReturnsTrue()
    {
        var model = new TestModel { Name = "Valid" };
        var ctx = new EditContext(model);

        Assert.True(ctx.Validate());
        Assert.Empty(ctx.ValidationMessages);
    }

    [Fact]
    public async Task EditForm_RendersFormTag()
    {
        var form = new EditForm
        {
            Model = new TestModel { Name = "Test" },
            Action = "/submit",
            Method = "post",
            CssClass = "my-form"
        };
        var html = await form.RenderAsync();

        Assert.Contains("<form", html);
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("action=\"/submit\"", html);
        Assert.Contains("class=\"my-form\"", html);
    }

    [Fact]
    public async Task InputText_RendersInputElement()
    {
        var input = new InputText
        {
            Value = "hello",
            Name = "username",
            Id = "txt-user",
            Placeholder = "Enter name",
            CssClass = "form-control",
            Required = true
        };
        var html = await input.RenderAsync();

        Assert.Contains("<input", html);
        Assert.Contains("type=\"text\"", html);
        Assert.Contains("name=\"username\"", html);
        Assert.Contains("id=\"txt-user\"", html);
        Assert.Contains("value=\"hello\"", html);
        Assert.Contains("placeholder=\"Enter name\"", html);
        Assert.Contains("class=\"form-control\"", html);
        Assert.Contains("required", html);
    }

    [Fact]
    public async Task InputNumber_RendersNumberInput()
    {
        var input = new InputNumber<int>
        {
            Value = 42,
            Name = "age",
            Min = "0",
            Max = "120"
        };
        var html = await input.RenderAsync();

        Assert.Contains("type=\"number\"", html);
        Assert.Contains("name=\"age\"", html);
        Assert.Contains("value=\"42\"", html);
        Assert.Contains("min=\"0\"", html);
        Assert.Contains("max=\"120\"", html);
    }

    [Fact]
    public async Task InputCheckbox_RendersCheckboxWithHiddenField()
    {
        var input = new InputCheckbox
        {
            Value = true,
            Name = "agree"
        };
        var html = await input.RenderAsync();

        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("checked", html);
        Assert.Contains("type=\"hidden\"", html);
        Assert.Contains("name=\"agree\"", html);
    }

    [Fact]
    public async Task InputTextArea_RendersTextareaElement()
    {
        var input = new InputTextArea
        {
            Value = "some text",
            Name = "description",
            Rows = 5,
            Placeholder = "Enter description"
        };
        var html = await input.RenderAsync();

        Assert.Contains("<textarea", html);
        Assert.Contains("name=\"description\"", html);
        Assert.Contains("rows=\"5\"", html);
        Assert.Contains("placeholder=\"Enter description\"", html);
        Assert.Contains("some text", html);
        Assert.Contains("</textarea>", html);
    }

    [Fact]
    public async Task ValidationMessage_RendersErrorForField()
    {
        // Test ValidationMessage by nesting it inside a parent with errors
        var component = new ValidationMessageTestParent { Name = null };
        component.PerformValidation();

        var html = await component.RenderAsync();
        // The validation message should show the error
        Assert.Contains("validation-message", html);
        Assert.Contains("required", html, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ValidationMessageTestParent : Microsoft.AspNetCore.Components.ComponentBase
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Name is required")]
        public string? Name { get; set; }

        public bool PerformValidation() => TryValidate(this);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<ValidationMessage>(0);
            builder.AddComponentParameter(1, "For", "Name");
            builder.CloseComponent();
        }
    }

    [Fact]
    public void NavigationManager_ToAbsoluteUri_ConvertsRelative()
    {
        var nav = new CosmoApiServer.Core.Http.NavigationManager();
        // Use reflection to set BaseUri since it's normally set by Initialize
        typeof(CosmoApiServer.Core.Http.NavigationManager)
            .GetProperty("BaseUri")!.SetValue(nav, "http://localhost:8080/");

        Assert.Equal("http://localhost:8080/foo/bar", nav.ToAbsoluteUri("/foo/bar"));
        Assert.Equal("https://external.com/path", nav.ToAbsoluteUri("https://external.com/path"));
    }

    [Fact]
    public void NavigationManager_ToBaseRelativePath_StripsBase()
    {
        var nav = new CosmoApiServer.Core.Http.NavigationManager();
        typeof(CosmoApiServer.Core.Http.NavigationManager)
            .GetProperty("BaseUri")!.SetValue(nav, "http://localhost:8080/");

        Assert.Equal("foo/bar", nav.ToBaseRelativePath("http://localhost:8080/foo/bar"));
    }

    [Fact]
    public void BindConverter_FormatValue_HandlesCommonTypes()
    {
        Assert.Equal("hello", BindConverter.FormatValue("hello"));
        Assert.Equal("42", BindConverter.FormatValue(42));
        Assert.Equal("3.14", BindConverter.FormatValue(3.14));
        Assert.Equal("true", BindConverter.FormatValue(true));
        Assert.Equal("", BindConverter.FormatValue((string?)null));
    }

    [Fact]
    public void BindConverter_TryConvertTo_ConvertsBetweenTypes()
    {
        Assert.True(BindConverter.TryConvertTo<int>("42", out var intVal));
        Assert.Equal(42, intVal);

        Assert.True(BindConverter.TryConvertTo<string>(123, out var strVal));
        Assert.Equal("123", strVal);
    }

    [Fact]
    public void EventCallbackFactory_Create_ReturnsWorkingCallback()
    {
        var called = false;
        var factory = new object(); // receiver
        var cb = EventCallback.Factory.Create(factory, (Action)(() => { called = true; }));
        cb.InvokeAsync().GetAwaiter().GetResult();
        Assert.True(called);
    }

    private sealed class TestModel
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Name is required")]
        public string? Name { get; set; }
    }

    // ── Change Detection Tests ───────────────────────────────────────────

    private sealed class ChangeTrackingModel
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int Age { get; set; }
    }

    [Fact]
    public void FieldIdentifier_Equality_SameModelSameField_AreEqual()
    {
        var model = new ChangeTrackingModel();
        var fi1 = new FieldIdentifier(model, "FirstName");
        var fi2 = new FieldIdentifier(model, "FirstName");

        Assert.Equal(fi1, fi2);
        Assert.True(fi1 == fi2);
        Assert.Equal(fi1.GetHashCode(), fi2.GetHashCode());
    }

    [Fact]
    public void FieldIdentifier_Equality_DifferentField_AreNotEqual()
    {
        var model = new ChangeTrackingModel();
        var fi1 = new FieldIdentifier(model, "FirstName");
        var fi2 = new FieldIdentifier(model, "LastName");

        Assert.NotEqual(fi1, fi2);
        Assert.True(fi1 != fi2);
    }

    [Fact]
    public void FieldIdentifier_Equality_DifferentModel_AreNotEqual()
    {
        var model1 = new ChangeTrackingModel();
        var model2 = new ChangeTrackingModel();
        var fi1 = new FieldIdentifier(model1, "FirstName");
        var fi2 = new FieldIdentifier(model2, "FirstName");

        Assert.NotEqual(fi1, fi2);
    }

    [Fact]
    public void EditContext_TakeSnapshot_CapturesOriginalValues()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice", Age = 30 };
        var ctx = new EditContext(model);

        // Initially not modified
        Assert.False(ctx.IsModified());
    }

    [Fact]
    public void EditContext_IsModified_DetectsPropertyChange()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice", Age = 30 };
        var ctx = new EditContext(model);

        // Change a property
        model.FirstName = "Bob";

        // IsModified does live comparison
        Assert.True(ctx.IsModified());
    }

    [Fact]
    public void EditContext_IsModified_SpecificField_DetectsChange()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice", LastName = "Smith", Age = 30 };
        var ctx = new EditContext(model);

        model.FirstName = "Bob";

        Assert.True(ctx.IsModified("FirstName"));
        Assert.False(ctx.IsModified("LastName"));
        Assert.False(ctx.IsModified("Age"));
    }

    [Fact]
    public void EditContext_NotifyFieldChanged_UpdatesFieldState()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice" };
        var ctx = new EditContext(model);

        model.FirstName = "Bob";
        ctx.NotifyFieldChanged("FirstName");

        var state = ctx.GetFieldState("FirstName");
        Assert.NotNull(state);
        Assert.True(state.IsModified);
        Assert.Equal("Bob", state.CurrentValue);
    }

    [Fact]
    public void EditContext_NotifyFieldChanged_FiresEvent()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice" };
        var ctx = new EditContext(model);

        FieldIdentifier? firedFor = null;
        ctx.OnFieldChanged += fi => firedFor = fi;

        model.FirstName = "Bob";
        ctx.NotifyFieldChanged("FirstName");

        Assert.NotNull(firedFor);
        Assert.Equal("FirstName", firedFor.Value.FieldName);
    }

    [Fact]
    public void EditContext_MarkAsUnmodified_ResetsAllFields()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice", LastName = "Smith" };
        var ctx = new EditContext(model);

        model.FirstName = "Bob";
        model.LastName = "Jones";
        ctx.NotifyFieldChanged("FirstName");
        ctx.NotifyFieldChanged("LastName");

        Assert.True(ctx.IsModified());

        ctx.MarkAsUnmodified();

        Assert.False(ctx.IsModified());
        Assert.False(ctx.IsModified("FirstName"));
        Assert.False(ctx.IsModified("LastName"));
    }

    [Fact]
    public void EditContext_MarkAsUnmodified_SingleField()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice", LastName = "Smith" };
        var ctx = new EditContext(model);

        model.FirstName = "Bob";
        model.LastName = "Jones";
        ctx.NotifyFieldChanged("FirstName");
        ctx.NotifyFieldChanged("LastName");

        ctx.MarkAsUnmodified("FirstName");

        Assert.False(ctx.IsModified("FirstName"));
        Assert.True(ctx.IsModified("LastName"));
    }

    [Fact]
    public void EditContext_GetModifiedFields_ReturnsCorrectFields()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice", LastName = "Smith", Age = 30 };
        var ctx = new EditContext(model);

        model.FirstName = "Bob";
        model.Age = 31;

        var modified = ctx.GetModifiedFields().ToList();

        Assert.Contains("FirstName", modified);
        Assert.Contains("Age", modified);
        Assert.DoesNotContain("LastName", modified);
    }

    [Fact]
    public void EditContext_FieldCssClass_ReturnsModifiedClass()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice" };
        var ctx = new EditContext(model);

        model.FirstName = "Bob";
        ctx.NotifyFieldChanged("FirstName");

        var css = ctx.FieldCssClass("FirstName");
        Assert.Contains("modified", css);
    }

    [Fact]
    public void EditContext_FieldCssClass_ReturnsValidAfterValidation()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice" };
        var ctx = new EditContext(model);

        ctx.NotifyFieldChanged("FirstName");
        ctx.Validate();

        var css = ctx.FieldCssClass("FirstName");
        Assert.Contains("valid", css);
    }

    [Fact]
    public void EditContext_FieldCssClass_ReturnsInvalidForFailedValidation()
    {
        var model = new TestModel { Name = null };
        var ctx = new EditContext(model);

        ctx.NotifyFieldChanged("Name");
        ctx.Validate();

        var css = ctx.FieldCssClass("Name");
        Assert.Contains("invalid", css);
    }

    [Fact]
    public void EditContext_Validate_FiresEvents()
    {
        var model = new TestModel { Name = null };
        var ctx = new EditContext(model);

        bool validationRequested = false;
        bool stateChanged = false;
        ctx.OnValidationRequested += () => validationRequested = true;
        ctx.OnValidationStateChanged += () => stateChanged = true;

        ctx.Validate();

        Assert.True(validationRequested);
        Assert.True(stateChanged);
    }

    [Fact]
    public void EditContext_RevertToOriginal_NoLongerModified()
    {
        var model = new ChangeTrackingModel { FirstName = "Alice" };
        var ctx = new EditContext(model);

        model.FirstName = "Bob";
        Assert.True(ctx.IsModified());

        // Revert
        model.FirstName = "Alice";
        Assert.False(ctx.IsModified());
    }

    [Fact]
    public void FieldState_TracksValidationAndModification()
    {
        var model = new TestModel { Name = "Valid" };
        var ctx = new EditContext(model);

        ctx.NotifyFieldChanged("Name");
        ctx.Validate();

        var state = ctx.GetFieldState("Name");
        Assert.NotNull(state);
        Assert.False(state.IsModified); // Value didn't actually change from snapshot
        Assert.True(state.IsValid);
        Assert.Null(state.ValidationMessage);
    }

    [Fact]
    public void FieldCssClassProvider_Default_CombinesClasses()
    {
        var model = new TestModel { Name = null };
        var ctx = new EditContext(model);

        model.Name = "something";
        ctx.NotifyFieldChanged("Name");
        ctx.Validate(); // Name was required but now has a value — but we changed it too

        var css = ctx.FieldCssClass("Name");
        // Name is modified (changed from null to "something") and valid
        Assert.Contains("modified", css);
        Assert.Contains("valid", css);
    }
}
