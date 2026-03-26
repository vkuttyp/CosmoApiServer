using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Razor.Internal;

namespace CosmoApiServer.Core.Controllers;

public static class ComponentScanner
{
    public static Type? _appType;
    public static Type? _mainLayoutType;

    // Cache reflection metadata per type to avoid per-request reflection
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _parameterCache = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _injectCache = new();

    public static void RegisterComponents(
        IEnumerable<Assembly> assemblies,
        RouteTable routeTable,
        IServiceProvider services)
    {
        foreach (var assembly in assemblies)
        {
            var allTypes = assembly.GetTypes();
            
            // Try to find App and MainLayout by convention
            _appType ??= allTypes.FirstOrDefault(t => t.Name == "App" && t.IsSubclassOf(typeof(Templates.ComponentBase)));
            _mainLayoutType ??= allTypes.FirstOrDefault(t => t.Name == "MainLayout" && t.IsSubclassOf(typeof(Microsoft.AspNetCore.Components.LayoutComponentBase)));

            var componentTypes = allTypes
                .Where(t => t.IsClass && !t.IsAbstract && (t.IsSubclassOf(typeof(Templates.ComponentBase)) || t.GetInterfaces().Contains(typeof(IComponent))));

            foreach (var type in componentTypes)
            {
                var routeAttrs = type.GetCustomAttributes<RouteAttribute>();
                foreach (var attr in routeAttrs)
                {
                    RegisterComponent(type, attr.Template, routeTable);
                }
            }
        }
    }

    private static PropertyInfo[] GetParameterProperties(Type type) =>
        _parameterCache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
             .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
             .ToArray());

    private static PropertyInfo[] GetInjectProperties(Type type) =>
        _injectCache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
             .Where(p => p.GetCustomAttribute<RazorInjectAttribute>() is not null || 
                         p.GetCustomAttribute<InjectAttribute>() is not null)
             .ToArray());

    private static void RegisterComponent(Type type, string template, RouteTable routeTable)
    {
        // Avoid duplicate registrations for same method/template (e.g. if already registered)
        // We match exactly on template string here
        
        // Pre-cache reflection metadata at registration time
        var paramProps = GetParameterProperties(type);
        var injectProps = GetInjectProperties(type);

        RequestDelegate handler = async ctx =>
        {
            var component = (Templates.ComponentBase)ActivatorUtilities.CreateInstance(ctx.RequestServices, type);
            component.HttpContext = ctx;

            // Resolve @inject properties from DI
            foreach (var prop in injectProps)
            {
                var service = ctx.RequestServices.GetService(prop.PropertyType);
                if (service is not null)
                {
                    // Initialize NavigationManager with the current context
                    if (service is NavigationManager nav)
                        nav.Initialize(ctx);
                    prop.SetValue(component, service);
                }
            }

            // Bind [Parameter] properties from Route, Query, and Form (using cached metadata)
            CosmoApiServer.Core.Http.MultipartForm? form = null;
            if (ctx.Request.Method == CosmoApiServer.Core.Http.HttpMethod.POST)
            {
                form = await ctx.Request.ReadFormAsync();
            }

            foreach (var prop in paramProps)
            {
                if (ctx.Request.RouteValues.TryGetValue(prop.Name, out var rv))
                {
                    prop.SetValue(component, ConvertValue(rv, prop.PropertyType));
                }
                else if (ctx.Request.Query.TryGetValue(prop.Name, out var qv))
                {
                    prop.SetValue(component, ConvertValue(qv, prop.PropertyType));
                }
                else if (form != null)
                {
                    var isComplex = !prop.PropertyType.IsPrimitive && 
                                    prop.PropertyType != typeof(string) && 
                                    !prop.PropertyType.IsValueType;

                    if (form.Fields.TryGetValue(prop.Name, out var fv))
                    {
                        prop.SetValue(component, ConvertValue(fv, prop.PropertyType));
                    }
                    else if (isComplex)
                    {
                        // Complex object binding from form fields (e.g. Model.Name)
                        var obj = prop.GetValue(component);
                        if (obj == null)
                        {
                            try {
                                obj = Activator.CreateInstance(prop.PropertyType);
                                prop.SetValue(component, obj);
                            } catch (Exception ex) { 
                                Console.WriteLine($"[ERROR] Could not create instance of {prop.PropertyType.Name}: {ex.Message}");
                            }
                        }

                        if (obj != null)
                        {
                            Console.WriteLine($"[DEBUG] Binding complex property '{prop.Name}' of type '{obj.GetType().Name}'");
                            foreach (var field in form.Fields)
                            {
                                var fieldName = field.Key;
                                // Support both "PropertyName" and "ModelName.PropertyName"
                                if (fieldName.StartsWith(prop.Name + ".", StringComparison.OrdinalIgnoreCase))
                                {
                                    fieldName = fieldName.Substring(prop.Name.Length + 1);
                                }

                                var subProp = obj.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                if (subProp != null && subProp.CanWrite)
                                {
                                    var converted = ConvertValue(field.Value, subProp.PropertyType);
                                    Console.WriteLine($"[DEBUG]   -> Setting {obj.GetType().Name}.{subProp.Name} = '{converted}'");
                                    subProp.SetValue(obj, converted);
                                }
                            }
                        }
                    }
                }
            }

            // Automatic Validation for routable components (using cached metadata)
            {
                foreach (var prop in paramProps)
                {
                    var val = prop.GetValue(component);
                    if (val != null)
                    {
                        ModelValidator.Validate(val, component.ModelState);
                    }
                }
                ModelValidator.Validate(component, component.ModelState);
            }

            // Automatic Wrapping: Component -> MainLayout -> App
            if (_appType != null)
            {
                var app = (Templates.ComponentBase)ActivatorUtilities.CreateInstance(ctx.RequestServices, _appType);
                app.HttpContext = ctx;
                
                var childContentProp = _appType.GetProperty("ChildContent");
                if (childContentProp != null)
                {
                    var componentHtml = await component.RenderAsync();
                    childContentProp.SetValue(app, (Microsoft.AspNetCore.Components.RenderFragment)(builder => 
                    {
                        if (_mainLayoutType != null)
                        {
                            builder.OpenComponent(0, _mainLayoutType);
                            builder.AddComponentParameter(1, "Body", (Microsoft.AspNetCore.Components.RenderFragment)(bodyBuilder => 
                            {
                                bodyBuilder.AddMarkupContent(0, componentHtml);
                            }));
                            builder.CloseComponent();
                        }
                        else
                        {
                            builder.AddMarkupContent(0, componentHtml);
                        }
                    }));
                }
                
                await app.RenderToResponseAsync(ctx.Response);
            }
            else if (_mainLayoutType != null)
            {
                var layout = (Microsoft.AspNetCore.Components.LayoutComponentBase)ActivatorUtilities.CreateInstance(ctx.RequestServices, _mainLayoutType);
                layout.HttpContext = ctx;
                var componentHtml = await component.RenderAsync();
                layout.Body = (builder) => builder.AddMarkupContent(0, componentHtml);
                await layout.RenderToResponseAsync(ctx.Response);
            }
            else
            {
                await component.RenderToResponseAsync(ctx.Response);
            }
        };

        routeTable.Add(Http.HttpMethod.GET, template, handler);
        routeTable.Add(Http.HttpMethod.POST, template, handler);
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        if (string.IsNullOrEmpty(value)) 
        {
            if (targetType == typeof(string)) return string.Empty;
            return null;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (underlyingType == typeof(string)) return value;
            if (underlyingType == typeof(int))    return int.TryParse(value, out var i) ? i : 0;
            if (underlyingType == typeof(long))   return long.TryParse(value, out var l) ? l : 0L;
            if (underlyingType == typeof(bool))   
            {
                if (bool.TryParse(value, out var b)) return b;
                if (value == "true,false") return true; // Handle checkbox quirk
                return value == "true" || value == "1" || value == "on";
            }
            if (underlyingType == typeof(Guid))   return Guid.TryParse(value, out var g) ? g : Guid.Empty;
            if (underlyingType.IsEnum)            return Enum.Parse(underlyingType, value, true);

            return System.Convert.ChangeType(value, underlyingType);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to convert '{value}' to {targetType.Name}: {ex.Message}");
            return null;
        }
    }
}
