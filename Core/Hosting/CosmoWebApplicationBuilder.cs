using System.Reflection;
using CosmoApiServer.Core.Auth;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CosmoApiServer.Core.Hosting;

public sealed class CosmoWebApplicationBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly MiddlewarePipeline _middlewarePipeline = new();
    private readonly List<Assembly> _controllerAssemblies = [];
    private readonly List<Assembly> _componentAssemblies = [];
    private readonly ServerOptions _options = new();
    private readonly IConfiguration _configuration;

    public IServiceCollection Services => _services;
    public ServerOptions ServerOptions => _options;
    public IConfiguration Configuration => _configuration;
    public static CosmoWebApplicationBuilder Create(string[]? args = null) => new(args);

    public CosmoWebApplicationBuilder(string[]? args = null)
    {
        // Default configuration loading
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var basePath = AppContext.BaseDirectory;

        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{env}.json", optional: true)
            .AddEnvironmentVariables();

        if (args != null && args.Length > 0)
        {
            configBuilder.AddCommandLine(args);
        }

        _configuration = configBuilder.Build();


        if (File.Exists(Path.Combine(basePath, "appsettings.json")))
        {
        }

        _services.AddSingleton(_configuration);
        _services.AddLogging();
        _services.AddScoped<Http.NavigationManager>();

        // ── ASP.NET Core Compatibility: Parse Port from 'Urls' ────────────────
        var urls = _configuration["Urls"] ?? _configuration["ASPNETCORE_URLS"] ?? _configuration["DOTNET_URLS"];
        if (!string.IsNullOrEmpty(urls))
        {
            var firstUrl = urls.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstUrl != null)
            {
                var lastColon = firstUrl.LastIndexOf(':');
                if (lastColon != -1 && int.TryParse(firstUrl.Substring(lastColon + 1), out int p))
                {
                    _options.Port = p;
                }
            }
        }
    }

    private string? _openApiPath;
    private OpenApiInfo? _openApiInfo;

    public static CosmoWebApplicationBuilder Create() => new();

    public CosmoWebApplicationBuilder UseLogging()
    {
        _middlewarePipeline.UseInstance(new LoggingMiddleware());
        return this;
    }

    public CosmoWebApplicationBuilder UseExceptionHandler()
    {
        _middlewarePipeline.UseInstance(new GlobalExceptionHandlerMiddleware());
        return this;
    }

    public CosmoWebApplicationBuilder UseStaticFiles(string rootPath = "wwwroot")
    {
        _middlewarePipeline.UseInstance(new StaticFileMiddleware(rootPath));
        return this;
    }

    public CosmoWebApplicationBuilder UseCors(Action<CorsOptions>? configure = null)
    {
        var opts = new CorsOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new CorsMiddleware(opts));
        return this;
    }

    public CosmoWebApplicationBuilder UseRateLimiting(Action<RateLimitOptions>? configure = null)
    {
        var opts = new RateLimitOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new RateLimitingMiddleware(opts));
        return this;
    }

    public CosmoWebApplicationBuilder UseCsrf(Action<CsrfOptions>? configure = null)
    {
        var opts = new CsrfOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new CsrfMiddleware(opts));
        return this;
    }

    public CosmoWebApplicationBuilder UseHttpsRedirection(Action<HttpsRedirectionOptions>? configure = null)
    {
        var opts = new HttpsRedirectionOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new HttpsRedirectionMiddleware(opts));
        return this;
    }

    public CosmoWebApplicationBuilder UseHsts(Action<HstsOptions>? configure = null)
    {
        var opts = new HstsOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new HstsMiddleware(opts));
        return this;
    }

    public CosmoWebApplicationBuilder UseResponseCompression(Action<ResponseCompressionOptions>? configure = null)
    {
        var opts = new ResponseCompressionOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new ResponseCompressionMiddleware(opts));
        return this;
    }

    public CosmoWebApplicationBuilder UseOpenApi(string path = "/openapi.json", Action<OpenApiInfo>? configure = null)
    {
        _openApiPath = path;
        _openApiInfo = new OpenApiInfo();
        configure?.Invoke(_openApiInfo);
        return this;
    }

    public CosmoWebApplicationBuilder UseSwaggerUI(string path = "/swagger", string? openApiUrl = null)
    {
        _middlewarePipeline.UseInstance(new SwaggerUIMiddleware(path, openApiUrl ?? _openApiPath ?? "/openapi.json"));
        return this;
    }

    public CosmoWebApplicationBuilder UseMiddleware<T>() where T : IMiddleware
    {
        _services.AddTransient(typeof(T));
        _middlewarePipeline.Use(next => ctx =>
        {
            var instance = (T)ctx.RequestServices.GetRequiredService(typeof(T));
            return instance.InvokeAsync(ctx, next);
        });
        return this;
    }

    public CosmoWebApplicationBuilder UseMiddleware(IMiddleware middleware)
    {
        _middlewarePipeline.UseInstance(middleware);
        return this;
    }

    public CosmoWebApplicationBuilder UseJwtAuthentication(JwtOptions options)
    {
        Console.WriteLine($"[JWT Setup] Secret Length: {options.Secret.Length}");
        Console.WriteLine($"[JWT Setup] Issuer: {options.Issuer}");
        Console.WriteLine($"[JWT Setup] Audience: {options.Audience}");
        
        _services.AddSingleton(options);
        _services.AddSingleton<JwtService>();
        _middlewarePipeline.UseInstance(new JwtMiddleware());
        return this;
    }

    public CosmoWebApplicationBuilder UseJwtAuthentication(Action<JwtOptions> configure)
    {
        var options = new JwtOptions();
        configure(options);
        return UseJwtAuthentication(options);
    }

    public CosmoWebApplicationBuilder AddControllers()
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        if (!_controllerAssemblies.Contains(callingAssembly))
            _controllerAssemblies.Add(callingAssembly);
        return this;
    }

    public CosmoWebApplicationBuilder AddControllersFromAssembly(Assembly assembly)
    {
        if (!_controllerAssemblies.Contains(assembly))
            _controllerAssemblies.Add(assembly);
        return this;
    }

    public CosmoWebApplicationBuilder AddRazorComponents()
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        if (!_componentAssemblies.Contains(callingAssembly))
            _componentAssemblies.Add(callingAssembly);
        return this;
    }

    public CosmoWebApplicationBuilder AddRazorComponentsFromAssembly(Assembly assembly)
    {
        if (!_componentAssemblies.Contains(assembly))
            _componentAssemblies.Add(assembly);
        return this;
    }

    public CosmoWebApplicationBuilder ListenOn(int port)
    {
        _options.Port = port;
        return this;
    }

    public CosmoWebApplicationBuilder UseHttps(string certificatePath, string? password = null)
    {
        _options.CertificatePath = certificatePath;
        _options.CertificatePassword = password;
        return this;
    }

    public CosmoWebApplicationBuilder UseHttp2()
    {
        _options.EnableHttp2 = true;
        return this;
    }

    public CosmoWebApplication Build()
    {
        var routeTable = new RouteTable();
        _services.AddSingleton(routeTable);

        if (_openApiPath != null && _openApiInfo != null)
        {
            var controllerTypes = _controllerAssemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(ControllerBase)));
            
            var spec = OpenApiGenerator.Generate(controllerTypes, _openApiInfo);
            _middlewarePipeline.UseInstance(new OpenApiMiddleware(_openApiPath, spec));
        }

        var provider = _services.BuildServiceProvider();

        return new CosmoWebApplication(
            provider,
            _middlewarePipeline,
            routeTable,
            _controllerAssemblies,
            _componentAssemblies,
            _options);
    }
}
