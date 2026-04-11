using System.Net.Http.Json;
using System.Text.Json;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class ViteFrontendOptions
{
    public string HtmlTemplatePath { get; set; } = "frontend/index.html";
    public string ManifestPath { get; set; } = "wwwroot/.vite/manifest.json";
    public string EntryName { get; set; } = "src/main.ts";
    public string AppElementHtml { get; set; } = "<div id=\"app\"></div>";
    public string[] ExcludedPrefixes { get; set; } = ["/api", "/openapi.json", "/swagger", "/health"];
    public string? DevServerUrl { get; set; }
    public string? SsrEndpointUrl { get; set; }
    public Func<ViteRenderContext, ValueTask<ViteRenderResult?>>? RenderAsync { get; set; }
}

public sealed class ViteRenderContext
{
    public required HttpContext HttpContext { get; init; }
}

public sealed class ViteRenderResult
{
    public string? HeadHtml { get; init; }
    public string? AppHtml { get; init; }
    public object? InitialState { get; init; }
    public string? StateVariableName { get; init; } = "__COSMO_VITE_STATE__";
    public string? BodyEndHtml { get; init; }
}

/// <summary>
/// Renders a Vite app shell using either a dev server URL or a production manifest.
/// </summary>
public sealed class ViteFrontendMiddleware : IMiddleware
{
    private readonly string _htmlTemplatePath;
    private readonly string _manifestPath;
    private readonly string _entryName;
    private readonly string _appElementHtml;
    private readonly string[] _excludedPrefixes;
    private readonly string? _devServerUrl;
    private readonly string? _ssrEndpointUrl;
    private readonly Func<ViteRenderContext, ValueTask<ViteRenderResult?>>? _renderAsync;
    private static readonly HttpClient SsrClient = new();

    public ViteFrontendMiddleware(ViteFrontendOptions options)
    {
        _htmlTemplatePath = Path.GetFullPath(options.HtmlTemplatePath);
        _manifestPath = Path.GetFullPath(options.ManifestPath);
        _entryName = options.EntryName;
        _appElementHtml = options.AppElementHtml;
        _devServerUrl = string.IsNullOrWhiteSpace(options.DevServerUrl) ? null : options.DevServerUrl.TrimEnd('/');
        _ssrEndpointUrl = string.IsNullOrWhiteSpace(options.SsrEndpointUrl) ? null : options.SsrEndpointUrl;
        _renderAsync = options.RenderAsync;
        _excludedPrefixes = options.ExcludedPrefixes
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(static p => p.StartsWith('/') ? p : "/" + p)
            .ToArray();
    }

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!ShouldHandle(context.Request) || !File.Exists(_htmlTemplatePath))
        {
            await next(context);
            return;
        }

        var renderResult = await ResolveRenderResultAsync(context);

        var template = await File.ReadAllTextAsync(_htmlTemplatePath, context.RequestAborted);
        var tags = BuildAssetTags();
        var html = template
            .Replace("<!--app-head-->", MergeHead(tags, renderResult), StringComparison.Ordinal)
            .Replace("<!--app-html-->", renderResult?.AppHtml ?? _appElementHtml, StringComparison.Ordinal)
            .Replace("<!--app-state-->", BuildStateScript(renderResult), StringComparison.Ordinal)
            .Replace("<!--app-body-end-->", renderResult?.BodyEndHtml ?? string.Empty, StringComparison.Ordinal);

        context.Response.StatusCode = 200;
        context.Response.WriteText(html, "text/html; charset=utf-8");
    }

    private async ValueTask<ViteRenderResult?> ResolveRenderResultAsync(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(_ssrEndpointUrl))
        {
            var payload = new
            {
                path = context.Request.Path,
                queryString = context.Request.QueryString,
                query = context.Request.Query,
                headers = context.Request.Headers
            };

            using var response = await SsrClient.PostAsJsonAsync(_ssrEndpointUrl, payload, context.RequestAborted);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ViteRenderResult>(cancellationToken: context.RequestAborted);
            return result;
        }

        if (_renderAsync is null)
            return null;

        return await _renderAsync(new ViteRenderContext { HttpContext = context });
    }

    private static string MergeHead(string assetTags, ViteRenderResult? renderResult)
    {
        if (string.IsNullOrWhiteSpace(renderResult?.HeadHtml))
            return assetTags;

        if (string.IsNullOrWhiteSpace(assetTags))
            return renderResult.HeadHtml!;

        return assetTags + Environment.NewLine + renderResult.HeadHtml;
    }

    private static string BuildStateScript(ViteRenderResult? renderResult)
    {
        if (renderResult?.InitialState is null || string.IsNullOrWhiteSpace(renderResult.StateVariableName))
            return string.Empty;

        var json = JsonSerializer.Serialize(renderResult.InitialState);
        return $"<script>window.{renderResult.StateVariableName} = {json};</script>";
    }

    private string BuildAssetTags()
    {
        if (!string.IsNullOrWhiteSpace(_devServerUrl))
        {
            return $$"""
<script type="module" src="{{_devServerUrl}}/@vite/client"></script>
<script type="module" src="{{_devServerUrl}}/{{_entryName}}"></script>
""";
        }

        if (!File.Exists(_manifestPath))
            throw new InvalidOperationException($"Vite manifest not found at '{_manifestPath}'. Build the frontend or set {nameof(ViteFrontendOptions.DevServerUrl)}.");

        var manifest = JsonSerializer.Deserialize<Dictionary<string, ViteManifestEntry>>(
            File.ReadAllText(_manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

        if (!manifest.TryGetValue(_entryName, out var entry) || string.IsNullOrWhiteSpace(entry.File))
            throw new InvalidOperationException($"Vite manifest entry '{_entryName}' was not found in '{_manifestPath}'.");

        var cssFiles = new HashSet<string>(StringComparer.Ordinal);
        var imports = new List<string>();
        CollectImports(manifest, entry, imports, cssFiles, new HashSet<string>(StringComparer.Ordinal));

        var lines = new List<string>();
        foreach (var importFile in imports)
            lines.Add($"<link rel=\"modulepreload\" crossorigin href=\"/{importFile}\">");
        foreach (var cssFile in cssFiles)
            lines.Add($"<link rel=\"stylesheet\" href=\"/{cssFile}\">");
        lines.Add($"<script type=\"module\" crossorigin src=\"/{entry.File}\"></script>");

        return string.Join(Environment.NewLine, lines);
    }

    private static void CollectImports(
        IReadOnlyDictionary<string, ViteManifestEntry> manifest,
        ViteManifestEntry entry,
        List<string> imports,
        HashSet<string> cssFiles,
        HashSet<string> visited)
    {
        if (!visited.Add(entry.File))
            return;

        if (entry.Imports is not null)
        {
            foreach (var import in entry.Imports)
            {
                if (!manifest.TryGetValue(import, out var importEntry) || string.IsNullOrWhiteSpace(importEntry.File))
                    continue;

                imports.Add(importEntry.File);
                CollectImports(manifest, importEntry, imports, cssFiles, visited);
            }
        }

        if (entry.Css is not null)
        {
            foreach (var css in entry.Css)
                cssFiles.Add(css);
        }
    }

    private bool ShouldHandle(HttpRequest request)
    {
        if (request.Method != CosmoApiServer.Core.Http.HttpMethod.GET &&
            request.Method != CosmoApiServer.Core.Http.HttpMethod.HEAD)
        {
            return false;
        }

        var path = string.IsNullOrWhiteSpace(request.Path) ? "/" : request.Path;
        if (HasFileExtension(path) || IsExcluded(path))
            return false;

        if (request.Headers.TryGetValue("Accept", out var accept) &&
            !string.IsNullOrWhiteSpace(accept) &&
            !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
            !accept.Contains("*/*", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private bool IsExcluded(string path)
    {
        foreach (var prefix in _excludedPrefixes)
        {
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasFileExtension(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        var lastDot = path.LastIndexOf('.');
        return lastDot > lastSlash;
    }

    private sealed class ViteManifestEntry
    {
        public string File { get; set; } = string.Empty;
        public string[]? Css { get; set; }
        public string[]? Imports { get; set; }
    }
}
