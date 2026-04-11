using CosmoApiServer.Core.Hosting;

var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../"));
var frontendRoot = Path.Combine(projectRoot, "frontend");
var wwwroot = Path.Combine(projectRoot, "wwwroot");
var devServerUrl = Environment.GetEnvironmentVariable("VITE_DEV_SERVER_URL");
var ssrServerUrl = Environment.GetEnvironmentVariable("VITE_SSR_SERVER_URL");

var builder = CosmoWebApplicationBuilder.Create()
    .ListenOn(8080)
    .UseLogging()
    .UseStaticFiles(wwwroot)
    .UseViteFrontend(options =>
    {
        options.HtmlTemplatePath = Path.Combine(frontendRoot, "index.html");
        options.ManifestPath = Path.Combine(wwwroot, ".vite", "manifest.json");
        options.EntryName = "src/entry-client.ts";
        options.DevServerUrl = devServerUrl;
        options.SsrEndpointUrl = ssrServerUrl;
        options.RenderAsync = string.IsNullOrWhiteSpace(ssrServerUrl)
            ? context =>
            {
                var path = context.HttpContext.Request.Path;
                var pageTitle = path switch
                {
                    "/dashboard" => "Dashboard | Cosmo Vue Server",
                    "/about" => "About | Cosmo Vue Server",
                    _ => "Cosmo Vue Server"
                };

                var initialState = new
                {
                    route = string.IsNullOrWhiteSpace(path) ? "/" : path,
                    dashboard = new
                    {
                        title = "Cosmo Vue Server",
                        latency = "0.24 ms p50",
                        transport = "Raw sockets -> pipelines -> Vue SPA",
                        highlights = new[]
                        {
                            "Vue frontend with history-mode routing",
                            "CosmoApiServer JSON APIs under /api",
                            "Static asset hosting with Vite manifest integration"
                        }
                    }
                };

                return ValueTask.FromResult<ViteRenderResult?>(new ViteRenderResult
                {
                    HeadHtml = $"<title>{pageTitle}</title>",
                    InitialState = initialState
                });
            }
            : null;
    });

var app = builder.Build();

app.MapGet("/api/health", async ctx =>
    await ctx.Response.WriteJsonAsync(new { status = "healthy", time = DateTime.UtcNow }));

app.MapGet("/api/dashboard", async ctx =>
    await ctx.Response.WriteJsonAsync(new
    {
        title = "Cosmo Vue Server",
        latency = "0.24 ms p50",
        transport = "Raw sockets -> pipelines -> Vue SPA",
        highlights = new[]
        {
            "Vue frontend with history-mode routing",
            "CosmoApiServer JSON APIs under /api",
            "Static asset hosting with Vite manifest integration"
        }
    }));

Console.WriteLine("Cosmo Vue Server running on http://localhost:8080");
Console.WriteLine(string.IsNullOrWhiteSpace(devServerUrl)
    ? "Serving built Vite assets from wwwroot."
    : $"Using Vite dev server at {devServerUrl}.");
Console.WriteLine(string.IsNullOrWhiteSpace(ssrServerUrl)
    ? "Using in-process shell rendering."
    : $"Using external SSR bridge at {ssrServerUrl}.");
app.Run();
