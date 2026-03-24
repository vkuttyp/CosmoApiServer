using System;
using System.Text;
using System.Buffers;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using CosmoApiServer.Core.Templates;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Razor.Hosting
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RazorCompiledItemAttribute(Type type, string kind, string identifier) : Attribute
    {
        public Type Type { get; } = type;
        public string Kind { get; } = kind;
        public string Identifier { get; } = identifier;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RazorSourceChecksumAttribute(string checksumAlgorithm, string checksum, string identifier) : Attribute
    {
        public string ChecksumAlgorithm { get; } = checksumAlgorithm;
        public string Checksum { get; } = checksum;
        public string Identifier { get; } = identifier;
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RazorCompiledItemMetadataAttribute(string key, string value) : Attribute
    {
        public string Key { get; } = key;
        public string Value { get; } = value;
    }
}

namespace Microsoft.AspNetCore.Mvc.ApplicationParts
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class RelatedAssemblyAttribute(string assemblyName) : Attribute
    {
        public string AssemblyName { get; } = assemblyName;
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class ProvideApplicationPartFactoryAttribute(string typeName) : Attribute
    {
        public string TypeName { get; } = typeName;
    }
}

namespace Microsoft.AspNetCore.Mvc.Razor.Internal
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class RazorInjectAttribute : Attribute;
}

namespace Microsoft.AspNetCore.Mvc.ViewFeatures
{
    public interface IModelExpressionProvider;
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
    public abstract class ComponentBase : CosmoApiServer.Core.Templates.ComponentBase, IComponent
    {
        protected virtual void BuildRenderTree(Rendering.RenderTreeBuilder builder) { }

        protected override async ValueTask BuildRenderTreeAsync(StringBuilder buffer)
        {
            var builder = new Rendering.RenderTreeBuilder(buffer);
            this._activeBuilder = builder;
            
            var field = typeof(Rendering.RenderTreeBuilder).GetField("_componentStack", BindingFlags.NonPublic | BindingFlags.Instance);
            var stack = (Stack<CosmoApiServer.Core.Templates.ComponentBase>)field!.GetValue(builder)!;
            stack.Push(this);

            BuildRenderTree(builder);
            
            // ASYNC DRAIN: Process the frames recorded by the builder
            await builder.ProcessAsync();
            
            stack.Pop(); 
            builder.CloseAll();
        }
    }

    public abstract class LayoutComponentBase : ComponentBase
    {
        [Parameter]
        public RenderFragment Body { get; set; }
    }

    public interface IComponent;

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class RouteAttribute(string template) : Attribute
    {
        public string Template { get; } = template;
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ParameterAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class CascadingParameterAttribute : Attribute;

    public delegate ValueTask RenderFragment(Rendering.RenderTreeBuilder builder);
    public delegate ValueTask RenderFragment<TValue>(Rendering.RenderTreeBuilder builder, TValue value);

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
            AddComponentParameter
        }

        internal struct RenderFrame
        {
            public RenderFrameType Type;
            public string? Text;
            public object? Value;
            public Type? ComponentType;
        }

        public sealed class RenderTreeBuilder(StringBuilder buffer)
        {
            private readonly Stack<string> _elementStack = new();
            private readonly Stack<CosmoApiServer.Core.Templates.ComponentBase> _componentStack = new();
            private readonly List<RenderFrame> _frames = new(128);
            private bool _inTag;

            private void Write(string? s)
            {
                if (s == null) return;
                // Since RenderTreeBuilder here works with StringBuilder (to match old CSHTML generator),
                // we just append the string. If we update to byte-based generator, we'd use Utf8LiteralCache.
                buffer.Append(s);
            }

            private void CloseOpeningTag()
            {
                if (_inTag)
                {
                    Write(">");
                    _inTag = false;
                }
            }

            public void AddContent(int sequence, string? text) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.AddContent, Text = text });

            public void AddContent(int sequence, object? content) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.AddContent, Value = content });

            public void AddMarkupContent(int sequence, string? markup) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.AddMarkupContent, Text = markup });
            
            public void OpenElement(int sequence, string elementName) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.OpenElement, Text = elementName });

            public void CloseElement() =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.CloseElement });
            
            public void AddAttribute(int sequence, string name, string? value) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });

            public void AddAttribute(int sequence, string name, object? value) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.AddAttribute, Text = name, Value = value });

            public void OpenComponent<TComponent>(int sequence) where TComponent : IComponent =>
                OpenComponent(sequence, typeof(TComponent));

            public void OpenComponent(int sequence, Type componentType) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.OpenComponent, ComponentType = componentType });

            public void CloseComponent() =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.CloseComponent });

            public void AddComponentParameter(int sequence, string name, object? value) =>
                _frames.Add(new RenderFrame { Type = RenderFrameType.AddComponentParameter, Text = name, Value = value });

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
                            buffer.Append($"<{frame.Text}");
                            _elementStack.Push(frame.Text!);
                            _inTag = true;
                            break;

                        case RenderFrameType.CloseElement:
                            if (_inTag) { buffer.Append(">"); _inTag = false; }
                            if (_elementStack.Count > 0)
                            {
                                var name = _elementStack.Pop();
                                buffer.Append($"</{name}>");
                            }
                            break;

                        case RenderFrameType.AddContent:
                            CloseOpeningTag();
                            if (frame.Text != null)
                            {
                                buffer.Append(CosmoApiServer.Core.Templates.HtmlEncoder.Encode(frame.Text));
                            }
                            else if (frame.Value != null)
                            {
                                if (frame.Value is string s) buffer.Append(CosmoApiServer.Core.Templates.HtmlEncoder.Encode(s));
                                else if (frame.Value is RenderFragment fragment) await fragment(this);
                                else if (frame.Value is CosmoApiServer.Core.Templates.HtmlString html) buffer.Append(html.ToString());
                                else buffer.Append(CosmoApiServer.Core.Templates.HtmlEncoder.Encode(frame.Value.ToString()));
                            }
                            break;

                        case RenderFrameType.AddMarkupContent:
                            CloseOpeningTag();
                            buffer.Append(frame.Text);
                            break;

                        case RenderFrameType.AddAttribute:
                            if (_inTag)
                            {
                                if (frame.Value is bool b)
                                {
                                    if (b) buffer.Append($" {frame.Text}");
                                }
                                else
                                {
                                    buffer.Append($" {frame.Text}=\"{CosmoApiServer.Core.Templates.HtmlEncoder.Encode(frame.Value?.ToString())}\"");
                                }
                            }
                            break;

                        case RenderFrameType.OpenComponent:
                            CloseOpeningTag();
                            var parent = _componentStack.Count > 0 ? _componentStack.Peek() : null;
                            var component = (parent != null && parent.HttpContext != null) 
                                ? (CosmoApiServer.Core.Templates.ComponentBase)ActivatorUtilities.CreateInstance(parent.HttpContext.RequestServices, frame.ComponentType!)
                                : (CosmoApiServer.Core.Templates.ComponentBase)Activator.CreateInstance(frame.ComponentType!)!;

                            component._buffer = parent?._buffer;
                            if (parent != null)
                            {
                                component.Parent = parent;
                                component.HttpContext = parent.HttpContext;
                            }
                            _componentStack.Push(component);
                            pendingComponent = component;
                            break;

                        case RenderFrameType.CloseComponent:
                            if (_componentStack.Count > 0)
                            {
                                var comp = _componentStack.Pop();
                                // Populate Cascading Parameters
                                foreach (var prop in comp.GetType().GetProperties())
                                {
                                    if (prop.GetCustomAttribute<CascadingParameterAttribute>() != null)
                                    {
                                        var val = FindCascadingValue(comp, prop.PropertyType);
                                        if (val != null) prop.SetValue(comp, val);
                                    }
                                }
                                // Resolve @inject properties from DI
                                if (comp.HttpContext?.RequestServices is not null)
                                {
                                    foreach (var prop in comp.GetType().GetProperties())
                                    {
                                        if (prop.GetCustomAttribute<Microsoft.AspNetCore.Mvc.Razor.Internal.RazorInjectAttribute>() != null)
                                        {
                                            var svc = comp.HttpContext.RequestServices.GetService(prop.PropertyType);
                                            if (svc is CosmoApiServer.Core.Http.NavigationManager nav)
                                                nav.Initialize(comp.HttpContext);
                                            if (svc != null) prop.SetValue(comp, svc);
                                        }
                                    }
                                }
                                await comp.RenderAsync();
                            }
                            break;

                        case RenderFrameType.AddComponentParameter:
                            if (pendingComponent != null)
                            {
                                var prop = pendingComponent.GetType().GetProperty(frame.Text!);
                                prop?.SetValue(pendingComponent, frame.Value);
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
                    // Check if the parent is a CascadingValue<T> component that provides the requested type
                    var currentType = current.GetType();
                    if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(CascadingValue<>))
                    {
                        var valueProp = currentType.GetProperty("Value");
                        if (valueProp != null && type.IsAssignableFrom(valueProp.PropertyType))
                        {
                            var val = valueProp.GetValue(current);
                            if (val != null) return val;
                        }
                    }

                    // Check if the parent itself matches the requested type
                    if (type.IsAssignableFrom(currentType)) return current;

                    // Check well-known properties (ModelState, EditContext, etc.)
                    var msProp = currentType.GetProperty("ModelState");
                    if (msProp != null && type.IsAssignableFrom(msProp.PropertyType))
                    {
                        var val = msProp.GetValue(current);
                        if (val != null) return val;
                    }

                    // Check EditForm's Context property
                    if (current is EditForm form && type == typeof(EditContext) && form.Context is not null)
                        return form.Context;

                    current = current.Parent;
                }
                return null;
            }

            internal void CloseAll()
            {
                while (_elementStack.Count > 0)
                {
                    var name = _elementStack.Pop();
                    buffer.Append($"</{name}>");
                }
            }
        }
    }
}
