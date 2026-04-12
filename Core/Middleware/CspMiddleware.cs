using System.Security.Cryptography;
using CosmoApiServer.Core.Http;

namespace CosmoApiServer.Core.Middleware;

public sealed class CspOptions
{
    /// <summary>
    /// Use <c>{nonce}</c> anywhere in a directive value; it is replaced with a
    /// per-request random nonce at runtime. The same nonce is stored in
    /// <see cref="CspMiddleware.NonceItemKey"/> on <see cref="HttpContext.Items"/>
    /// so that other middleware (e.g. ViteFrontendMiddleware) can embed it in
    /// inline script tags.
    /// </summary>
    public string[]  DefaultSrc     { get; set; } = ["'self'"];
    public string[]  ScriptSrc      { get; set; } = ["'self'", "'nonce-{nonce}'"];
    public string[]  StyleSrc       { get; set; } = ["'self'", "'unsafe-inline'"];
    public string[]  ImgSrc         { get; set; } = ["'self'", "data:", "blob:"];
    public string[]  FontSrc        { get; set; } = ["'self'"];
    public string[]  ConnectSrc     { get; set; } = ["'self'"];
    public string[]  WorkerSrc      { get; set; } = ["'self'", "blob:"];
    public string[]  ManifestSrc    { get; set; } = ["'self'"];
    public string[]  FrameAncestors { get; set; } = ["'none'"];
    public string[]  FormAction     { get; set; } = ["'self'"];

    /// <summary>Additional raw directives appended verbatim (e.g. "upgrade-insecure-requests").</summary>
    public string[]  Extra          { get; set; } = [];

    /// <summary>
    /// When true, the policy is sent as <c>Content-Security-Policy-Report-Only</c> instead of
    /// enforcing it. Useful during rollout.
    /// </summary>
    public bool ReportOnly { get; set; } = false;
}

/// <summary>
/// Generates a per-request CSP nonce, stores it in <c>HttpContext.Items</c>, and emits the
/// <c>Content-Security-Policy</c> (or Report-Only) header.
///
/// The nonce is automatically picked up by <see cref="ViteFrontendMiddleware"/> and injected
/// into inline script tags (<c>window.__COSMO_VITE_STATE__</c>, asset module scripts).
///
/// Register before <see cref="ViteFrontendMiddleware"/>:
/// <code>
/// builder.UseCsp(o =>
/// {
///     o.ConnectSrc = ["'self'", "ws://localhost:*"]; // allow HMR WebSocket in dev
/// });
/// builder.UseViteFrontend(...);
/// </code>
/// </summary>
public sealed class CspMiddleware(CspOptions options) : IMiddleware
{
    /// <summary>Key used to store the nonce string in <see cref="HttpContext.Items"/>.</summary>
    public const string NonceItemKey = "cosmo.csp.nonce";

    public async ValueTask InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var nonce = GenerateNonce();
        context.Items[NonceItemKey] = nonce;

        // Build the policy string, substituting {nonce} with the actual nonce value.
        var policy = BuildPolicy(nonce);
        var headerName = options.ReportOnly
            ? "Content-Security-Policy-Report-Only"
            : "Content-Security-Policy";

        context.Response.Headers[headerName] = policy;

        await next(context);
    }

    private string BuildPolicy(string nonce)
    {
        var directives = new List<string>();

        Add(directives, "default-src",     options.DefaultSrc,     nonce);
        Add(directives, "script-src",      options.ScriptSrc,      nonce);
        Add(directives, "style-src",       options.StyleSrc,       nonce);
        Add(directives, "img-src",         options.ImgSrc,         nonce);
        Add(directives, "font-src",        options.FontSrc,        nonce);
        Add(directives, "connect-src",     options.ConnectSrc,     nonce);
        Add(directives, "worker-src",      options.WorkerSrc,      nonce);
        Add(directives, "manifest-src",    options.ManifestSrc,    nonce);
        Add(directives, "frame-ancestors", options.FrameAncestors, nonce);
        Add(directives, "form-action",     options.FormAction,     nonce);

        foreach (var extra in options.Extra)
            directives.Add(extra);

        return string.Join("; ", directives);
    }

    private static void Add(List<string> directives, string name, string[] values, string nonce)
    {
        if (values.Length == 0) return;
        var resolved = string.Join(' ', values.Select(v => v.Replace("{nonce}", nonce, StringComparison.Ordinal)));
        directives.Add($"{name} {resolved}");
    }

    private static string GenerateNonce() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)); // 24-char base64
}
