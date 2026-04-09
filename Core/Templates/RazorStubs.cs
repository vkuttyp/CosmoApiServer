using System.Buffers;
using System.Text;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Microsoft.AspNetCore.Mvc.Razor.Internal
{
    public sealed class RazorInjectAttribute : Attribute;
}

namespace Microsoft.AspNetCore.Mvc
{
    public interface IUrlHelper;
    public interface IViewComponentHelper;
}

namespace Microsoft.AspNetCore.Mvc.Rendering
{
    public interface IHtmlHelper<TModel>;
    public interface IJsonHelper;
}

namespace Microsoft.AspNetCore.Components
{
    public struct MarkupString(string value)
    {
        public string Value { get; } = value;
        public override string ToString() => Value ?? string.Empty;
    }

    public struct ElementReference(string id)
    {
        public string Id { get; } = id;
    }

    public abstract class ComponentBase : CosmoApiServer.Core.Templates.ComponentBase, IComponent
    {
        protected virtual void BuildRenderTree(Rendering.RenderTreeBuilder builder) { }

        protected override async ValueTask BuildRenderTreeAsync(IBufferWriter<byte> buffer)
        {
            this._buffer = buffer;
            var builder = new Rendering.RenderTreeBuilder(buffer, this);
            this._activeBuilder = builder;

            BuildRenderTree(builder);
            await builder.ProcessAsync();
            builder.CloseAll();
        }
    }

    public abstract class LayoutComponentBase : ComponentBase
    {
        [Parameter]
        public RenderFragment Body { get; set; }
    }

    public interface IComponent { }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ParameterAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class CascadingParameterAttribute : Attribute;
    
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RouteAttribute(string template) : Attribute { public string Template { get; } = template; }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class LayoutAttribute(Type layoutType) : Attribute { public Type LayoutType { get; } = layoutType; }

    public sealed class InjectAttribute : Attribute;

    public delegate void RenderFragment(Rendering.RenderTreeBuilder builder);
    public delegate RenderFragment RenderFragment<TValue>(TValue value);

    namespace Rendering
    {
        internal enum RenderFrameType
        {
            OpenElement,
            CloseElement,
            AddContent,
            AddMarkupContent,
            AddAttribute,
            OpenComponent,
            CloseComponent,
            AddComponentParameter,
            ElementReferenceCapture,
            ComponentReferenceCapture,
            Region
        }

        internal struct RenderFrame
        {
            public RenderFrameType Type;
            public string? Text;
            public object? Value;
            public Type? ComponentType;
            public Action<object>? ReferenceCaptureAction;
        }

        public sealed class RenderTreeBuilder
        {
            private readonly Stack<string> _elementStack = new();
            private readonly Stack<CosmoApiServer.Core.Templates.ComponentBase> _componentStack = new();
            private readonly List<RenderFrame> _frames = new(128);
            private readonly IBufferWriter<byte> _buffer;
            private readonly CosmoApiServer.Core.Templates.ComponentBase? _owner;
            private bool _inTag;

            public RenderTreeBuilder(IBufferWriter<byte> buffer) { _buffer = buffer; }
            public RenderTreeBuilder(IBufferWriter<byte> buffer, CosmoApiServer.Core.Templates.ComponentBase owner)
            {
                _buffer = buffer;
                _owner = owner;
                _componentStack.Push(owner);
            }

            private void Write(string? s) { if (s != null) _buffer.Write(CosmoApiServer.Core.Templates.Utf8LiteralCache.GetEncoded(s)); }
            private void CloseOpeningTag() { if (_inTag) { Write(">"); _inTag = false; } }

            public void AddContent(int sequence, string? text) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddContent, Text = text });
            public void AddContent(int sequence, RenderFragment? fragment) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddContent, Value = fragment });
            public void AddContent(int sequence, MarkupString markupContent) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddMarkupContent, Text = markupContent.Value });
            public void AddContent(int sequence, object? content) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddContent, Value = content });
            public void AddMarkupContent(int sequence, string? markup) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddMarkupContent, Text = markup });
            public void OpenElement(int sequence, string elementName) => _frames.Add(new RenderFrame { Type = RenderFrameType.OpenElement, Text = elementName });
            public void CloseElement() => _frames.Add(new RenderFrame { Type = RenderFrameType.CloseElement });
            
            public void AddAttribute(int sequence, string name, string? value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });
            public void AddAttribute(int sequence, string name, bool value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });
            public void AddAttribute(int sequence, string name, object? value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });
            public void AddAttribute(int sequence, string name, Action? value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });
            public void AddAttribute(int sequence, string name, Action<object>? value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });
            public void AddAttribute(int sequence, string name, EventCallback value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });
            
            public void AddAttribute(int sequence, string name) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name });
            public void AddAttribute(string name, string? value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });
            public void AddAttribute(string name, bool value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });
            public void AddAttribute(string name, object? value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });

            public void AddMultipleAttributes(int sequence, IEnumerable<KeyValuePair<string, object>>? attributes)
            {
                if (attributes == null) return;
                foreach (var attr in attributes) AddAttribute(sequence, attr.Key, attr.Value);
            }

            public void OpenComponent<TComponent>(int sequence) where TComponent : IComponent => OpenComponent(sequence, typeof(TComponent));
            public void OpenComponent(int sequence, Type componentType) => _frames.Add(new RenderFrame { Type = RenderFrameType.OpenComponent, ComponentType = componentType });
            public void CloseComponent() => _frames.Add(new RenderFrame { Type = RenderFrameType.CloseComponent });
            public void AddComponentParameter(int sequence, string name, object? value) => _frames.Add(new RenderFrame { Type = RenderFrameType.AddComponentParameter, Text = name, Value = value });

            public void OpenRegion(int sequence) { }
            public void CloseRegion() { }

            public void AddElementReferenceCapture(int sequence, Action<ElementReference> action) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.ElementReferenceCapture, ReferenceCaptureAction = obj => action((ElementReference)obj) });

            public void AddComponentReferenceCapture(int sequence, Action<object> action) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.ComponentReferenceCapture, ReferenceCaptureAction = action });

            public async ValueTask ProcessAsync()
            {
                CosmoApiServer.Core.Templates.ComponentBase? pendingComponent = null;
                for (int i = 0; i < _frames.Count; i++)
                {
                    var frame = _frames[i];
                    switch (frame.Type)
                    {
                        case RenderFrameType.OpenElement:
                            CloseOpeningTag();
                            pendingComponent = null;
                            Write("<"); Write(frame.Text);
                            _elementStack.Push(frame.Text!);
                            _inTag = true;
                            break;
                        case RenderFrameType.CloseElement:
                            if (_inTag) { Write(">"); _inTag = false; }
                            if (_elementStack.Count > 0) { var name = _elementStack.Pop(); Write("</"); Write(name); Write(">"); }
                            break;
                        case RenderFrameType.AddContent:
                            CloseOpeningTag();
                            if (frame.Text != null) await CosmoApiServer.Core.Templates.HtmlEncoder.EncodeToWriterAsync(frame.Text, _buffer);
                            else if (frame.Value != null)
                            {
                                if (frame.Value is string s) await CosmoApiServer.Core.Templates.HtmlEncoder.EncodeToWriterAsync(s, _buffer);
                                else if (frame.Value is RenderFragment fragment)
                                {
                                    var beforeCount = _frames.Count;
                                    fragment(this);
                                    var addedCount = _frames.Count - beforeCount;
                                    if (addedCount > 0)
                                    {
                                        var newFrames = _frames.GetRange(beforeCount, addedCount);
                                        _frames.RemoveRange(beforeCount, addedCount);
                                        _frames.InsertRange(i + 1, newFrames);
                                    }
                                }
                                else await CosmoApiServer.Core.Templates.HtmlEncoder.EncodeToWriterAsync(frame.Value, _buffer);
                            }
                            break;
                        case RenderFrameType.AddMarkupContent:
                            CloseOpeningTag();
                            Write(frame.Text);
                            break;
                        case RenderFrameType.AddAttribute:
                            if (_inTag)
                            {
                                if (frame.Value is bool b) { if (b) { Write(" "); Write(frame.Text); } }
                                else { Write(" "); Write(frame.Text); Write("=\""); await CosmoApiServer.Core.Templates.HtmlEncoder.EncodeToWriterAsync(frame.Value, _buffer); Write("\""); }
                            }
                            else if (pendingComponent != null)
                            {
                                var prop = pendingComponent.GetType().GetProperty(frame.Text!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                if (prop != null) prop.SetValue(pendingComponent, frame.Value);
                            }
                            break;
                        case RenderFrameType.OpenComponent:
                            CloseOpeningTag();
                            var parent = _componentStack.Count > 0 ? _componentStack.Peek() : _owner;
                            var component = (parent != null && parent.HttpContext != null) 
                                ? (CosmoApiServer.Core.Templates.ComponentBase)ActivatorUtilities.CreateInstance(parent.HttpContext.RequestServices, frame.ComponentType!)
                                : (CosmoApiServer.Core.Templates.ComponentBase)Activator.CreateInstance(frame.ComponentType!)!;
                            component._buffer = _buffer;
                            if (parent != null) { component.Parent = parent; component.HttpContext = parent.HttpContext; }
                            _componentStack.Push(component);
                            pendingComponent = component;
                            break;
                        case RenderFrameType.CloseComponent:
                            if (_componentStack.Count > 0)
                            {
                                var comp = _componentStack.Pop();
                                foreach (var prop in comp.GetType().GetProperties())
                                {
                                    if (prop.GetCustomAttribute<CascadingParameterAttribute>() != null)
                                    {
                                        var val = FindCascadingValue(comp, prop.PropertyType);
                                        if (val != null) prop.SetValue(comp, val);
                                    }
                                }
                                if (comp.HttpContext?.RequestServices is not null)
                                {
                                    foreach (var prop in comp.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                    {
                                        if (prop.GetCustomAttribute<Microsoft.AspNetCore.Mvc.Razor.Internal.RazorInjectAttribute>() != null || prop.GetCustomAttribute<InjectAttribute>() != null)
                                        {
                                            var svc = comp.HttpContext.RequestServices.GetService(prop.PropertyType);
                                            if (svc is CosmoApiServer.Core.Http.NavigationManager nav) nav.Initialize(comp.HttpContext);
                                            if (svc != null) prop.SetValue(comp, svc);
                                        }
                                    }
                                }
                                await comp.RenderAsync();
                                pendingComponent = null;
                            }
                            break;
                        case RenderFrameType.AddComponentParameter:
                            if (pendingComponent != null)
                            {
                                var prop = pendingComponent.GetType().GetProperty(frame.Text!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                if (prop != null) prop.SetValue(pendingComponent, frame.Value);
                            }
                            break;
                    }
                }
                _frames.Clear();
            }

            private object? FindCascadingValue(CosmoApiServer.Core.Templates.ComponentBase component, Type type)
            {
                var current = component.Parent;
                while (current != null)
                {
                    if (current is CascadingValue<object> cv)
                    {
                        var prop = cv.GetType().GetProperty("CascadingValues", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                        if (prop != null)
                        {
                            var dict = prop.GetValue(cv) as Dictionary<Type, object?>;
                            if (dict != null && dict.TryGetValue(type, out var val)) return val;
                        }
                    }
                    if (type == typeof(Dictionary<string, string>) && current.ModelState.Count > 0) return current.ModelState;
                    current = current.Parent;
                }
                return null;
            }

            public void CloseAll()
            {
                CloseOpeningTag();
                while (_elementStack.Count > 0) { var name = _elementStack.Pop(); Write("</"); Write(name); Write(">"); }
            }
        }
    }
}

namespace Microsoft.AspNetCore.Components.CompilerServices
{
    public static class RuntimeHelpers
    {
        public static T TypeCheck<T>(T value) => value;
    }
}
