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

    /// <summary>
    /// Serves a pre-built frontend SPA from <paramref name="outputPath"/>.
    /// Registers static file serving, response compression (Brotli/GZip), and an SPA
    /// fallback that returns <c>index.html</c> for unmatched client-side routes.
    /// This is the shared base used by all framework-specific integrations.
    /// </summary>
    public CosmoWebApplicationBuilder UseStaticFrontend(
        string outputPath,
        Action<SpaFallbackOptions>? configureFallback = null)
    {
        UseStaticFiles(outputPath);
        UseResponseCompression();
        return UseSpaFallback(opts =>
        {
            opts.RootPath = outputPath;
            configureFallback?.Invoke(opts);
        });
    }

    /// <summary>
    /// Serves a pre-built Nuxt SPA from <paramref name="outputPath"/> (default:
    /// <c>frontend/.output/public</c>). Delegates to <see cref="UseStaticFrontend"/>.
    /// </summary>
    public CosmoWebApplicationBuilder UseNuxtIntegrated(
        string outputPath = "frontend/.output/public",
        Action<SpaFallbackOptions>? configureFallback = null)
        => UseStaticFrontend(outputPath, configureFallback);

    /// <summary>
    /// Serves a pre-built Next.js static export from <paramref name="outputPath"/>
    /// (default: <c>frontend/out</c>, produced by <c>next build</c> with
    /// <c>output: 'export'</c> in <c>next.config.js</c>).
    /// For Next.js SSR use <see cref="UseReverseProxy"/> pointing at <c>next start</c>.
    /// </summary>
    public CosmoWebApplicationBuilder UseNextStaticExport(
        string outputPath = "frontend/out",
        Action<SpaFallbackOptions>? configureFallback = null)
        => UseStaticFrontend(outputPath, configureFallback);

    /// <summary>
    /// Serves a pre-built Angular application from <paramref name="outputPath"/>
    /// (default: <c>frontend/dist/browser</c>, produced by <c>ng build</c>).
    /// </summary>
    public CosmoWebApplicationBuilder UseAngularFrontend(
        string outputPath = "frontend/dist/browser",
        Action<SpaFallbackOptions>? configureFallback = null)
        => UseStaticFrontend(outputPath, configureFallback);

    /// <summary>
    /// Serves a pre-built SvelteKit application from <paramref name="outputPath"/>
    /// (default: <c>frontend/build</c>, produced by <c>vite build</c> with
    /// <c>adapter-static</c>). For <c>adapter-node</c> use <see cref="UseReverseProxy"/>.
    /// </summary>
    public CosmoWebApplicationBuilder UseSvelteKitStatic(
        string outputPath = "frontend/build",
        Action<SpaFallbackOptions>? configureFallback = null)
        => UseStaticFrontend(outputPath, configureFallback);

    /// <summary>
    /// Hosts a published Blazor WebAssembly application from <paramref name="outputPath"/>
    /// (default: <c>blazor/wwwroot</c>, the <c>wwwroot/</c> folder produced by
    /// <c>dotnet publish</c> on the Blazor WASM project).
    ///
    /// Extends <see cref="UseStaticFrontend"/> with Blazor-specific behaviour:
    /// <list type="bullet">
    ///   <item>Serves pre-compressed <c>_framework/*.br</c> / <c>*.gz</c> files directly
    ///   with <c>Content-Encoding</c> set — skipping on-the-fly compression for the large
    ///   WASM/assembly bundles that Blazor pre-compresses at publish time.</item>
    ///   <item>Adds <c>application/wasm</c> as the MIME type for <c>.wasm</c> files —
    ///   browsers refuse to instantiate WASM served as <c>application/octet-stream</c>.</item>
    ///   <item>Sets <c>Cache-Control: public, max-age=31536000, immutable</c> on all
    ///   <c>_framework/</c> files — they are content-addressed by Blazor's linker.</item>
    /// </list>
    /// </summary>
    public CosmoWebApplicationBuilder UseBlazorWasm(
        string outputPath = "blazor/wwwroot",
        Action<SpaFallbackOptions>? configureFallback = null)
    {
        _middlewarePipeline.UseInstance(new BlazorWasmMiddleware(outputPath));
        return UseStaticFrontend(outputPath, configureFallback);
    }

    /// <summary>
    /// Pre-configured <see cref="UseViteDevProxy"/> for React + Vite dev mode.
    /// Proxies <c>/@vite</c>, <c>/@fs</c>, <c>/@id</c>, and <c>/@react-refresh</c>
    /// to the Vite dev server so the browser can use a single origin.
    /// </summary>
    public CosmoWebApplicationBuilder UseReactDevProxy(
        string devServerUrl = "http://127.0.0.1:5173",
        Action<ViteDevProxyOptions>? configure = null)
        => UseViteDevProxy(o =>
        {
            o.DevServerUrl    = devServerUrl;
            o.ProxiedPrefixes = ["/@vite", "/@fs", "/@id", "/@react-refresh"];
            configure?.Invoke(o);
        });

    /// <summary>
    /// Pre-configured <see cref="UseViteDevProxy"/> for Next.js dev mode.
    /// Proxies <c>/__next</c>, <c>/_next</c>, <c>/webpack-hmr</c>, and related
    /// Next.js internal paths to the dev server.
    /// </summary>
    public CosmoWebApplicationBuilder UseNextDevProxy(
        string devServerUrl = "http://127.0.0.1:3000",
        Action<ViteDevProxyOptions>? configure = null)
        => UseViteDevProxy(o =>
        {
            o.DevServerUrl    = devServerUrl;
            o.ProxiedPrefixes = ["/__next", "/_next", "/__nextjs_original-stack-frame", "/webpack-hmr"];
            configure?.Invoke(o);
        });

    /// <summary>
    /// Pre-configured <see cref="UseReverseProxy"/> for Angular dev mode (<c>ng serve</c>).
    /// Forwards all non-API traffic to the Angular dev server. Angular does not use
    /// Vite's module graph paths, so a full reverse proxy is required rather than
    /// selective path forwarding.
    /// </summary>
    public CosmoWebApplicationBuilder UseAngularDevProxy(
        string devServerUrl = "http://127.0.0.1:4200",
        string[] excludedPrefixes = default!,
        Action<ReverseProxyOptions>? configure = null)
        => UseReverseProxy(o =>
        {
            o.Routes.Add(new ProxyRoute
            {
                PathPrefix       = "/",
                Destination      = devServerUrl,
                ExcludedPrefixes = excludedPrefixes ?? ["/api", "/health"]
            });
            configure?.Invoke(o);
        });

    /// <summary>
    /// Pre-configured <see cref="UseViteDevServer"/> for Next.js. Sets the ready
    /// pattern to the string Next.js prints when compilation succeeds.
    /// </summary>
    public CosmoWebApplicationBuilder UseNextDevServer(Action<ViteDevServerOptions>? configure = null)
        => UseViteDevServer(o =>
        {
            o.Command      = "npm";
            o.Arguments    = "run dev";
            o.ReadyPattern = "Ready";
            o.LogPrefix    = "[next]";
            configure?.Invoke(o);
        });

    /// <summary>
    /// Pre-configured <see cref="UseViteDevServer"/> for Angular (<c>ng serve</c>).
    /// Sets the ready pattern to the line Angular CLI prints after initial compilation.
    /// </summary>
    public CosmoWebApplicationBuilder UseAngularDevServer(Action<ViteDevServerOptions>? configure = null)
        => UseViteDevServer(o =>
        {
            o.Command      = "npx";
            o.Arguments    = "ng serve --host 127.0.0.1";
            o.ReadyPattern = "Application bundle generation complete";
            o.LogPrefix    = "[angular]";
            configure?.Invoke(o);
        });

    /// <summary>
    /// Forwards Vite/Nuxt dev-server paths (virtual modules, HMR client, <c>/@vite</c>,
    /// <c>/_nuxt</c>, etc.) to a running dev server, enabling single-port dev mode.
    /// Register before <c>UseViteFrontend</c>.
    /// </summary>
    public CosmoWebApplicationBuilder UseViteDevProxy(Action<ViteDevProxyOptions>? configure = null)
    {
        var opts = new ViteDevProxyOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new ViteDevProxyMiddleware(opts));
        return this;
    }

    /// <summary>
    /// Starts the Vite or Nuxt dev server as a hosted service, eliminating the need for
    /// a separate shell script. The process is killed cleanly on app shutdown.
    /// </summary>
    public CosmoWebApplicationBuilder UseViteDevServer(Action<ViteDevServerOptions>? configure = null)
    {
        var opts = new ViteDevServerOptions();
        configure?.Invoke(opts);
        _services.AddSingleton<IHostedService>(new ViteDevServerService(opts));
        return this;
    }

    /// <summary>
    /// Emits a per-request CSP nonce and the <c>Content-Security-Policy</c> header.
    /// The nonce is injected automatically into inline script tags by <c>UseViteFrontend</c>.
    /// Register before <c>UseViteFrontend</c>.
    /// </summary>
    public CosmoWebApplicationBuilder UseCsp(Action<CspOptions>? configure = null)
    {
        var opts = new CspOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new CspMiddleware(opts));
        return this;
    }

    /// <summary>
    /// Routes-based reverse proxy supporting HTTP and WebSocket connections.
    /// Typical use: proxy all non-API traffic to a Nuxt SSR Node server.
    /// </summary>
    public CosmoWebApplicationBuilder UseReverseProxy(Action<ReverseProxyOptions>? configure = null)
    {
        var opts = new ReverseProxyOptions();
        configure?.Invoke(opts);
        _middlewarePipeline.UseInstance(new ReverseProxyMiddleware(opts));
        return this;
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

    public CosmoWebApplicationBuilder UseHttps(Func<string?, System.Security.Cryptography.X509Certificates.X509Certificate2?> certificateSelector)
    {
        _options.CertificateSelector = certificateSelector;
        return this;
    }

    public CosmoWebApplicationBuilder ListenHttpsOn(int port)
    {
        _options.HttpsPort = port;
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
