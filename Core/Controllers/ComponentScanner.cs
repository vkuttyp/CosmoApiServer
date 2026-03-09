using System.Reflection;
using System.Text;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using RenderFragment = Microsoft.AspNetCore.Components.RenderFragment;

namespace CosmoApiServer.Core.Controllers;

public static class ComponentScanner
{
    public static Type? _appType;
    public static Type? _mainLayoutType;

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

    private static void RegisterComponent(Type type, string template, RouteTable routeTable)
    {
        RequestDelegate handler = async ctx =>
        {
            var component = (Templates.ComponentBase)ActivatorUtilities.CreateInstance(ctx.RequestServices, type);
            component.HttpContext = ctx;

            // Bind properties from Route and Query
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<ParameterAttribute>() is null) continue;

                if (ctx.Request.RouteValues.TryGetValue(prop.Name, out var rv))
                {
                    prop.SetValue(component, Convert(rv, prop.PropertyType));
                }
                else if (ctx.Request.Query.TryGetValue(prop.Name, out var qv))
                {
                    prop.SetValue(component, Convert(qv, prop.PropertyType));
                }
            }

            // Automatic Validation for routable components
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<ParameterAttribute>() != null)
                {
                    var val = prop.GetValue(component);
                    if (val != null) ModelValidator.Validate(val, component.ModelState);
                }
            }
            ModelValidator.Validate(component, component.ModelState);

            // Automatic Wrapping: Component -> MainLayout -> App
            if (_appType != null)
            {
                var app = (Templates.ComponentBase)ActivatorUtilities.CreateInstance(ctx.RequestServices, _appType);
                app.HttpContext = ctx;
                
                var childContentProp = _appType.GetProperty("ChildContent");
                if (childContentProp != null)
                {
                    childContentProp.SetValue(app, (RenderFragment)(builder => 
                    {
                        if (_mainLayoutType != null)
                        {
                            builder.OpenComponent(0, _mainLayoutType);
                            builder.AddComponentParameter(1, "Body", (RenderFragment)(async bodyBuilder => 
                            {
                                await RenderToBuilder(component, bodyBuilder);
                            }));
                            builder.CloseComponent();
                        }
                        else
                        {
                            return RenderToBuilder(component, builder);
                        }
                        return ValueTask.CompletedTask;
                    }));
                }
                
                await app.RenderToResponseAsync(ctx.Response);
            }
            else if (_mainLayoutType != null)
            {
                var layout = (Microsoft.AspNetCore.Components.LayoutComponentBase)ActivatorUtilities.CreateInstance(ctx.RequestServices, _mainLayoutType);
                layout.HttpContext = ctx;
                layout.Body = (builder) => RenderToBuilder(component, builder);
                await layout.RenderToResponseAsync(ctx.Response);
            }
            else
            {
                await component.RenderToResponseAsync(ctx.Response);
            }
        };

        routeTable.Add(Http.HttpMethod.GET, template, handler);
    }

    private static async ValueTask RenderToBuilder(Templates.ComponentBase component, Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        // Set the builder's buffer on the component so it writes directly to it
        var field = typeof(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder).GetField("buffer", BindingFlags.NonPublic | BindingFlags.Instance);
        var buffer = (StringBuilder)field!.GetValue(builder)!;
        
        component._buffer = buffer;
        await component.RenderAsync();
    }

    private static object? Convert(string value, Type targetType)
    {
        if (string.IsNullOrEmpty(value)) return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(string)) return value;
        if (underlyingType == typeof(int))    return int.Parse(value);
        if (underlyingType == typeof(long))   return long.Parse(value);
        if (underlyingType == typeof(bool))   return bool.Parse(value);
        if (underlyingType == typeof(Guid))   return Guid.Parse(value);

        return System.Convert.ChangeType(value, underlyingType);
    }
}
