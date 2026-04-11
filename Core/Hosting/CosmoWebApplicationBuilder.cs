using System.Reflection;
using CosmoApiServer.Core.Auth;
using CosmoApiServer.Core.Auth.Authorization;
using CosmoApiServer.Core.Auth.OAuth;
using CosmoApiServer.Core.Caching;
using CosmoApiServer.Core.Controllers;
using CosmoApiServer.Core.Grpc;
using CosmoApiServer.Core.HealthChecks;
using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoApiServer.Core.ProblemDetails;
using CosmoApiServer.Core.Routing;
using CosmoApiServer.Core.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    public CosmoWebApplicationBuilder UseSpaFallback(Action<SpaFallbackOptions>? configure = null)
    {
        var options = new SpaFallbackOptions();
        configure?.Invoke(options);
        _middlewarePipeline.UseInstance(new SpaFallbackMiddleware(options));
        return this;
    }

    public CosmoWebApplicationBuilder UseVueFrontend(string rootPath = "wwwroot", Action<SpaFallbackOptions>? configure = null)
    {
        UseStaticFiles(rootPath);

        return UseSpaFallback(options =>
        {
            options.RootPath = rootPath;
            configure?.Invoke(options);
        });
    }

    public CosmoWebApplicationBuilder UseViteFrontend(Action<ViteFrontendOptions>? configure = null)
    {
        var options = new ViteFrontendOptions();
        configure?.Invoke(options);
        _middlewarePipeline.UseInstance(new ViteFrontendMiddleware(options));
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

    public CosmoWebApplicationBuilder UseHttp3()
    {
        _options.EnableHttp3 = true;
        return this;
    }

    // ── Health Checks ────────────────────────────────────────────────────────

    public HealthChecksBuilder AddHealthChecks()
    {
        var service = new HealthCheckService();
        _services.AddSingleton(service);
        return new HealthChecksBuilder(_services, service);
    }

    public CosmoWebApplicationBuilder UseHealthChecks(string path = "/health")
    {
        _middlewarePipeline.UseInstance(new HealthCheckMiddleware(path));
        return this;
    }

    // ── Problem Details (RFC 7807) ───────────────────────────────────────────

    public CosmoWebApplicationBuilder AddProblemDetails(Action<ProblemDetailsOptions>? configure = null)
    {
        var opts = new ProblemDetailsOptions();
        configure?.Invoke(opts);
        _services.AddSingleton(opts);
        _services.AddSingleton<IProblemDetailsService, DefaultProblemDetailsService>();
        return this;
    }

    // ── Memory Cache ─────────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddMemoryCache(Action<MemoryCacheOptions>? configure = null)
    {
        var opts = new MemoryCacheOptions();
        configure?.Invoke(opts);
        _services.AddSingleton<IMemoryCache>(new MemoryCache(opts));
        return this;
    }

    // ── IHttpClientFactory ───────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddHttpClient(string name, Action<HttpClient>? configure = null)
    {
        if (configure is not null)
            _services.AddHttpClient(name, configure);
        else
            _services.AddHttpClient(name);
        return this;
    }

    public CosmoWebApplicationBuilder AddHttpClient<TClient, TImplementation>()
        where TClient : class
        where TImplementation : class, TClient
    {
        _services.AddHttpClient<TClient, TImplementation>();
        return this;
    }

    // ── Policy-Based Authorization ───────────────────────────────────────────

    public CosmoWebApplicationBuilder AddAuthorization(Action<AuthorizationOptions>? configure = null)
    {
        var opts = new AuthorizationOptions();
        configure?.Invoke(opts);
        _services.AddSingleton(opts);
        _services.AddScoped<IAuthorizationService, DefaultAuthorizationService>();
        return this;
    }

    // ── Antiforgery ───────────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddAntiforgery(Action<AntiforgeryOptions>? configure = null)
    {
        var opts = new AntiforgeryOptions();
        configure?.Invoke(opts);
        _services.AddSingleton(opts);
        _services.AddSingleton<IAntiforgeryService, DefaultAntiforgeryService>();
        return this;
    }

    public CosmoWebApplicationBuilder UseAntiforgery()
    {
        _middlewarePipeline.Use(next => async ctx =>
        {
            var svc = ctx.RequestServices.GetService<IAntiforgeryService>();
            if (svc is not null)
                await new AntiforgeryMiddleware(svc).InvokeAsync(ctx, next);
            else
                await next(ctx);
        });
        return this;
    }

    // ── WebSockets ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enables WebSocket support. Raw WebSockets are always available via
    /// <c>context.AcceptWebSocketAsync()</c>; calling this method is optional
    /// but signals intent and matches the ASP.NET Core API surface.
    /// </summary>
    public CosmoWebApplicationBuilder UseWebSockets()
    {
        // WebSocket upgrades are handled natively by the transport.
        // No additional middleware needed — this method exists for API parity.
        return this;
    }

    // ── Exception Handlers ────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddExceptionHandler<T>() where T : class, IExceptionHandler
    {
        _services.AddSingleton<IExceptionHandler, T>();
        return this;
    }

    public CosmoWebApplicationBuilder AddExceptionHandler(IExceptionHandler handler)
    {
        _services.AddSingleton(handler);
        return this;
    }

    // ── Hosted Services ───────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddHostedService<T>() where T : class, IHostedService
    {
        _services.AddSingleton<IHostedService, T>();
        return this;
    }

    public CosmoWebApplicationBuilder AddHostedService<T>(Func<IServiceProvider, T> factory) where T : class, IHostedService
    {
        _services.AddSingleton<IHostedService>(factory);
        return this;
    }

    // ── IHttpContextAccessor ─────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddHttpContextAccessor()
    {
        _services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        return this;
    }

    // ── Request Timeouts ─────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder UseRequestTimeouts(Action<RequestTimeoutOptions>? configure = null)
    {
        var opts = new RequestTimeoutOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new RequestTimeoutMiddleware(opts));
        return this;
    }

    // ── Output Caching ────────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddOutputCache(Action<OutputCacheOptions>? configure = null)
    {
        var opts = new OutputCacheOptions();
        configure?.Invoke(opts);
        _services.AddSingleton(opts);
        _services.AddSingleton<IOutputCacheStore, InMemoryOutputCacheStore>();
        return this;
    }

    public CosmoWebApplicationBuilder UseOutputCaching()
    {
        _middlewarePipeline.Use(next => async ctx =>
        {
            var store = ctx.RequestServices.GetService<IOutputCacheStore>();
            var opts = ctx.RequestServices.GetService<OutputCacheOptions>() ?? new OutputCacheOptions();
            if (store is not null)
                await new OutputCachingMiddleware(store, opts).InvokeAsync(ctx, next);
            else
                await next(ctx);
        });
        return this;
    }

    // ── Response Caching ─────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder UseResponseCaching(Action<ResponseCachingOptions>? configure = null)
    {
        var opts = new ResponseCachingOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new ResponseCachingMiddleware(opts));
        return this;
    }

    // ── Sessions ─────────────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder UseSession(Action<SessionOptions>? configure = null)
    {
        var opts = new SessionOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new SessionMiddleware(opts));
        return this;
    }

    // ── IDistributedCache ────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddDistributedMemoryCache()
    {
        _services.AddSingleton<IDistributedCache, InMemoryDistributedCache>();
        return this;
    }

    public CosmoWebApplicationBuilder AddDistributedCache<TImpl>() where TImpl : class, IDistributedCache
    {
        _services.AddSingleton<IDistributedCache, TImpl>();
        return this;
    }

    // ── gRPC ─────────────────────────────────────────────────────────────────

    private Middleware.GrpcRouteRegistry? _grpcRegistry;

    public CosmoWebApplicationBuilder AddGrpc()
    {
        _grpcRegistry = new Middleware.GrpcRouteRegistry();
        _services.AddSingleton(_grpcRegistry);
        _middlewarePipeline.UseInstance(new Middleware.GrpcMiddleware(_grpcRegistry));
        return this;
    }

    // ── SignalR ───────────────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder AddSignalR()
    {
        _services.AddSingleton<SignalR.HubContextRegistry>();
        return this;
    }

    // ── Forwarded Headers ────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder UseForwardedHeaders(Action<ForwardedHeadersOptions>? configure = null)
    {
        var opts = new ForwardedHeadersOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new ForwardedHeadersMiddleware(opts));
        return this;
    }

    // ── Request Decompression ────────────────────────────────────────────────

    public CosmoWebApplicationBuilder UseRequestDecompression(Action<RequestDecompressionOptions>? configure = null)
    {
        var opts = new RequestDecompressionOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new RequestDecompressionMiddleware(opts));
        return this;
    }

    // ── Distributed Tracing (OpenTelemetry-compatible) ───────────────────────

    public CosmoWebApplicationBuilder UseTracing(Action<TracingOptions>? configure = null)
    {
        var opts = new TracingOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new TracingMiddleware(opts));
        return this;
    }

    // ── OAuth2 / OIDC ────────────────────────────────────────────────────────

    public CosmoWebApplicationBuilder UseOAuthAuthentication(Action<OAuthOptions> configure)
    {
        var opts = new OAuthOptions();
        configure(opts);
        _middlewarePipeline.UseInstance(new OAuthMiddleware(opts));
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
