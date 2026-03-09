using System;
using System.Text;
using System.Buffers;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Templates;

/// <summary>
/// Marks a property as a component parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ParameterAttribute : Attribute;

/// <summary>
/// A delegate that represents a piece of UI to be rendered.
/// </summary>
public delegate ValueTask RenderFragment(StringBuilder buffer);

/// <summary>
/// Base class for Razor Components in CosmoApiServer.
/// </summary>
public abstract class ComponentBase
{
    internal StringBuilder? _buffer;
    internal Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder? _activeBuilder;

    /// <summary>
    /// Gets or sets the parent component.
    /// </summary>
    public ComponentBase? Parent { get; set; }

    /// <summary>
    /// Gets or sets the HTTP context for the current request.
    /// </summary>
    public HttpContext HttpContext { get; set; } = null!;

    /// <summary>
    /// Collection of validation errors for the current request.
    /// </summary>
    public Dictionary<string, string> ModelState { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether the model state is valid.
    /// </summary>
    public bool IsValid => ModelState.Count == 0;

    /// <summary>
    /// Validates the given model using DataAnnotations.
    /// Errors are added to <see cref="ModelState"/>.
    /// </summary>
    protected bool TryValidate(object? model)
    {
        if (model is null) return true;
        return CosmoApiServer.Core.Controllers.ModelValidator.Validate(model, ModelState);
    }

    /// <summary>
    /// Lifecycle method called when the component is initialized.
    /// </summary>
    protected virtual ValueTask OnInitializedAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Lifecycle method called when parameters are set.
    /// </summary>
    protected virtual ValueTask OnParametersSetAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Executes the component and writes its output to the provided buffer.
    /// In .razor files, this will be overridden by the generated code.
    /// </summary>
    protected virtual ValueTask BuildRenderTreeAsync(StringBuilder buffer) => ValueTask.CompletedTask;

    /// <summary>
    /// Renders the component to a string.
    /// </summary>
    public async ValueTask<string> RenderAsync()
    {
        var isRoot = _buffer == null;
        if (isRoot) _buffer = new StringBuilder();
        
        await OnInitializedAsync();
        await OnParametersSetAsync();
        await BuildRenderTreeAsync(_buffer!);
        
        if (isRoot)
        {
            var result = _buffer!.ToString();
            _buffer = null;
            return result;
        }
        
        return string.Empty;
    }

    /// <summary>
    /// Renders the component directly to the CosmoApiServer HttpResponse.
    /// </summary>
    public async ValueTask RenderToResponseAsync(HttpResponse response)
    {
        response.Headers["Content-Type"] = "text/html; charset=utf-8";
        
        var html = await RenderAsync();
        response.WriteText(html, "text/html; charset=utf-8");
    }

    // ── Rendering methods ─────────────────────────────────────────────

    protected void WriteLiteral(string? value) => _buffer?.Append(value);

    protected void Write(object? value)
    {
        if (value == null || _buffer == null) return;

        if (value is HtmlString html)
            _buffer.Append(html.ToString());
        else if (value is RenderFragment fragment)
        {
            fragment(_buffer).GetAwaiter().GetResult();
        }
        else
        {
            var writer = new StringBuilderBufferWriter(_buffer);
            HtmlEncoder.EncodeToWriter(value, writer);
        }
    }

    protected void Write(string? value)
    {
        if (value == null || _buffer == null) return;
        var writer = new StringBuilderBufferWriter(_buffer);
        HtmlEncoder.EncodeToWriter(value, writer);
    }

    // Support for component nesting
    protected async ValueTask RenderComponentAsync<TComponent>(Action<TComponent>? configure = null) 
        where TComponent : ComponentBase, new()
    {
        var component = new TComponent();
        component.HttpContext = HttpContext;
        component.Parent = this;
        component._buffer = _buffer;
        configure?.Invoke(component);
        await component.RenderAsync();
    }

    private sealed class StringBuilderBufferWriter(StringBuilder sb) : IBufferWriter<byte>
    {
        public void Advance(int count) { }
        public Memory<byte> GetMemory(int sizeHint = 0) => new byte[sizeHint > 0 ? sizeHint : 4096];
        public Span<byte> GetSpan(int sizeHint = 0) => new byte[sizeHint > 0 ? sizeHint : 4096];
        public void Write(ReadOnlySpan<byte> value) => sb.Append(Encoding.UTF8.GetString(value));
    }
}
