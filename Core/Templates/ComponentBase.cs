using System;
using System.Text;
using System.Buffers;
using System.Runtime.CompilerServices;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Templates;

/// <summary>
/// Marks a property as a component parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ParameterAttribute : Attribute;

/// <summary>
/// A delegate that represents a piece of UI to be rendered directly to a buffer.
/// </summary>
public delegate ValueTask RenderToBufferDelegate(IBufferWriter<byte> buffer);

/// <summary>
/// Base class for Razor Components in CosmoApiServer.
/// </summary>
public abstract class ComponentBase
{
    internal IBufferWriter<byte>? _buffer;
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
    /// Dictionary of validation errors for the current component.
    /// </summary>
    public Dictionary<string, string> ModelState { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether the model state is valid.
    /// </summary>
    public bool IsValid => ModelState.Count == 0;

    /// <summary>
    /// Manually adds an error to the model state.
    /// </summary>
    public void AddError(string fieldName, string errorMessage) => ModelState[fieldName] = errorMessage;

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
    /// Validates an object against a Zod-like schema.
    /// Errors are added to <see cref="ModelState"/>.
    /// </summary>
    protected bool TryValidate<T>(T model, ObjectSchema<T> schema) where T : new()
    {
        var result = schema.SafeParse(model);
        if (!result.IsValid)
        {
            foreach (var err in result.Errors) ModelState[err.Key] = err.Value;
        }
        return result.IsValid;
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
    protected virtual ValueTask BuildRenderTreeAsync(IBufferWriter<byte> buffer) => ValueTask.CompletedTask;

    /// <summary>
    /// Renders the component to a string.
    /// </summary>
    public async ValueTask<string> RenderAsync()
    {
        var isRoot = _buffer == null;
        HttpResponse.TestBufferWriter? rootBuffer = null;
        if (isRoot)
        {
            rootBuffer = new HttpResponse.TestBufferWriter();
            _buffer = rootBuffer;
        }
        
        await OnInitializedAsync();
        await OnParametersSetAsync();
        await BuildRenderTreeAsync(_buffer!);
        
        if (isRoot)
        {
            var result = Encoding.UTF8.GetString(rootBuffer!.ToArray());
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
        
        if (response.BodyWriter != null)
        {
            _buffer = new ResponseBufferWriter(response);
            await OnInitializedAsync();
            await OnParametersSetAsync();
            await BuildRenderTreeAsync(_buffer);
            _buffer = null;
            response.End();
        }
        else
        {
            var html = await RenderAsync();
            response.WriteText(html, "text/html; charset=utf-8");
        }
    }

    // ── Rendering methods ─────────────────────────────────────────────

    protected void WriteLiteral(string? value)
    {
        if (value == null || _buffer == null) return;
        _buffer.Write(Utf8LiteralCache.GetEncoded(value));
    }

    protected async ValueTask Write(object? value)
    {
        if (value == null || _buffer == null) return;

        if (value is HtmlString html)
        {
            _buffer.Write(Utf8LiteralCache.GetEncoded(html.ToString()));
        }
        else if (value is RenderToBufferDelegate fragment)
        {
            await fragment(_buffer);
        }
        else
        {
            await HtmlEncoder.EncodeToWriterAsync(value, _buffer);
        }
    }

    protected async ValueTask Write(string? value)
    {
        if (value == null || _buffer == null) return;
        HtmlEncoder.EncodeToWriter(value, _buffer);
    }

    protected async ValueTask RenderComponentAsync<TComponent>(Action<TComponent>? configure = null) 
        where TComponent : ComponentBase, new()
    {
        var component = new TComponent();
        component.HttpContext = HttpContext;
        component.Parent = this;
        component._buffer = _buffer;
        configure?.Invoke(component);
        
        await component.OnInitializedAsync();
        await component.OnParametersSetAsync();
        await component.BuildRenderTreeAsync(_buffer!);
    }

    private sealed class ResponseBufferWriter(HttpResponse response) : IBufferWriter<byte>
    {
        private byte[]? _pending;

        public void Advance(int count)
        {
            if (_pending != null && count > 0)
            {
                response.Write(_pending.AsSpan(0, count));
                ArrayPool<byte>.Shared.Return(_pending);
                _pending = null;
            }
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            _pending = ArrayPool<byte>.Shared.Rent(sizeHint > 0 ? sizeHint : 4096);
            return _pending;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            _pending = ArrayPool<byte>.Shared.Rent(sizeHint > 0 ? sizeHint : 4096);
            return _pending;
        }

        public void Write(ReadOnlySpan<byte> value) => response.Write(value);
    }
}
