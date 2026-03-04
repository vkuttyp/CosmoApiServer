using System.Reflection;
using CosmoApiServer.Core.Auth;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CosmoApiServer.Core.Hosting;

/// <summary>
/// Builder for CosmoWebApplication. Analogous to WebApplicationBuilder in ASP.NET.
/// </summary>
public sealed class CosmoWebApplicationBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly MiddlewarePipeline _middlewarePipeline = new();
    private readonly List<Assembly> _controllerAssemblies = [];
    private readonly ServerOptions _options = new();
    private string? _openApiPath;
    private OpenApiInfo? _openApiInfo;

    public IServiceCollection Services => _services;
    public ServerOptions ServerOptions => _options;

    public static CosmoWebApplicationBuilder Create() => new();

    // ── Middleware ─────────────────────────────────────────────────────────

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

    // ── Authentication ─────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder UseJwtAuthentication(JwtOptions options)
    {
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

    // ── Controllers ────────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddControllers()
    {
        // Scan the calling assembly (entry point) automatically
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

    // ── Server config ──────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder ListenOn(int port)
    {
        _options.Port = port;
        return this;
    }

    /// <summary>
    /// Enable HTTPS. The certificate must be a PFX/PKCS#12 file.
    /// </summary>
    public CosmoWebApplicationBuilder UseHttps(string certificatePath, string? password = null)
    {
        _options.CertificatePath = certificatePath;
        _options.CertificatePassword = password;
        return this;
    }

    /// <summary>
    /// Advertise HTTP/2 (h2) via ALPN during TLS handshake.
    /// Must be combined with <see cref="UseHttps"/>.
    /// </summary>
    public CosmoWebApplicationBuilder UseHttp2()
    {
        _options.EnableHttp2 = true;
        return this;
    }

    // ── Build ──────────────────────────────────────────────────────────────

    public CosmoWebApplication Build()
    {
        // Register RouteTable in DI
        var routeTable = new RouteTable();
        _services.AddSingleton(routeTable);

        // OpenAPI generation (happens once at startup)
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
            _options);
    }
}
