using CosmoApiServer.Core.Http;

namespace CosmoS3;

/// <summary>
/// S3 request context. Wraps CosmoApiServer.Core HttpContext and provides parsed S3 request/response.
/// Replaces S3ServerLibrary.S3Context without any WatsonWebserver dependency.
/// </summary>
public class S3Context
{
    #region Public-Members

    /// <summary>Parsed S3 request (bucket, key, operation type, auth, etc.).</summary>
    public S3Request Request { get; }

    /// <summary>S3 response helper.</summary>
    public S3Response Response { get; }

    /// <summary>The underlying CosmoApiServer HTTP context.</summary>
    public HttpContext Http { get; }

    /// <summary>Application-supplied metadata (e.g. RequestMetadata after auth).</summary>
    public object? Metadata { get; set; } = null;

    #endregion

    #region Constructors

    /// <summary>
    /// Build an S3Context from a CosmoApiServer.Core HttpContext.
    /// </summary>
    /// <param name="ctx">HTTP context from the CosmoApiServer pipeline.</param>
    /// <param name="baseDomainFinder">Optional callback to resolve the base domain for virtual-hosted-style S3 URLs.</param>
    /// <param name="logger">Optional log sink.</param>
    public S3Context(HttpContext ctx, Func<string, string>? baseDomainFinder = null, Action<string>? logger = null)
    {
        Http = ctx ?? throw new ArgumentNullException(nameof(ctx));
        Request = new S3Request(ctx.Request, baseDomainFinder, logger);
        Response = new S3Response(this);
    }

    #endregion
}
