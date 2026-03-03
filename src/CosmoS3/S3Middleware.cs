using CosmoApiServer.Core.Http;
using CosmoApiServer.Core.Middleware;
using CosmoS3.Api.Admin;
using CosmoS3.Api.S3;
using CosmoS3.Classes;
using CosmoS3.Logging;
using CosmoS3.S3Objects;
using CosmoS3.Settings;
using System.Text;

namespace CosmoS3;

/// <summary>
/// CosmoApiServer pipeline middleware that handles S3-compatible requests.
/// Replaces the WatsonWebserver-based S3Server + Program wiring from Less3/StorageServer.
/// 
/// Usage: add to your CosmoApiServer pipeline and supply a configured SettingsBase.
///   pipeline.Use(new S3Middleware(settings));
/// </summary>
public sealed class S3Middleware : IMiddleware
{
    #region Private-Members

    private readonly SettingsBase _Settings;
    private readonly S3Logger _Logging;
    private readonly ConfigManager _Config;
    private readonly BucketManager _Buckets;
    private readonly AuthManager _Auth;
    private readonly ApiHandler _ApiHandler;
    private readonly AdminApiHandler _AdminApiHandler;
    private readonly CleanupManager _Cleanup;

    #endregion

    #region Constructor

    /// <summary>
    /// Initialise the S3 middleware and all sub-systems (config, buckets, auth, handlers).
    /// </summary>
    /// <param name="settings">CosmoS3 settings (database, storage, auth, region, etc.).</param>
    /// <param name="logLevel">Minimum log level written to stdout.</param>
    public S3Middleware(SettingsBase settings, LogLevel logLevel = LogLevel.Info)
    {
        _Settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // Inject settings into DataAccess before any subsystem initialises
        DataAccess.Initialize(_Settings);

        _Logging = new S3Logger("[CosmoS3] ", logLevel);

        // Ensure storage and temp directories exist
        if (!string.IsNullOrEmpty(_Settings.Storage.DiskDirectory))
            Directory.CreateDirectory(_Settings.Storage.DiskDirectory);
        if (!string.IsNullOrEmpty(_Settings.Storage.TempDirectory))
            Directory.CreateDirectory(_Settings.Storage.TempDirectory);

        _Logging.Info("Initializing configuration manager");
        _Config = new ConfigManager(_Settings, _Logging);

        _Logging.Info("Initializing bucket manager");
        _Buckets = new BucketManager(_Settings, _Logging, _Config);

        _Logging.Info("Initializing authentication manager");
        _Auth = new AuthManager(_Settings, _Logging, _Config, _Buckets);

        _Logging.Info("Initializing S3 API handler");
        _ApiHandler = new ApiHandler(_Settings, _Logging, _Config, _Buckets, _Auth);

        _Logging.Info("Initializing admin API handler");
        _AdminApiHandler = new AdminApiHandler(_Settings, _Logging, _Config, _Buckets, _Auth);

        _Logging.Info("Initializing cleanup manager");
        _Cleanup = new CleanupManager(_Settings, _Logging, _Config);

        _Logging.Info("CosmoS3 ready");
    }

    #endregion

    #region IMiddleware

    /// <summary>
    /// Intercepts every request. If the path starts with the admin key header prefix,
    /// routes to the admin handler; otherwise treats the request as an S3 API call.
    /// Pass to <c>next</c> if the request is not an S3 request (e.g. a health check on "/").
    /// </summary>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Build S3 context from the incoming CosmoApiServer HTTP context
        S3Context s3ctx = new S3Context(
            context,
            baseDomainFinder: _ApiHandler.FindMatchingBaseDomain,
            logger: _Logging.Debug);

        try
        {
            // Route admin requests separately (identified by the admin API key header)
            if (IsAdminRequest(context))
            {
                await _AdminApiHandler.Process(s3ctx);
                return;
            }

            // Dispatch based on the parsed S3 request type
            await DispatchS3Request(s3ctx);
        }
        catch (S3Exception s3ex)
        {
            _Logging.Warn("S3Exception: " + s3ex.Error?.Code + " – " + s3ex.Message);
            await s3ctx.Response.Send(s3ex.Error ?? new Error(ErrorCode.InternalError));
        }
        catch (Exception ex)
        {
            _Logging.Exception(nameof(InvokeAsync), ex);
            await s3ctx.Response.Send(new Error(ErrorCode.InternalError));
        }
    }

    #endregion

    #region Private-Methods

    private bool IsAdminRequest(HttpContext ctx)
    {
        return ctx.Request.Headers.TryGetValue(_Settings.HeaderApiKey, out var key)
            && key == _Settings.AdminApiKey;
    }

    private async Task DispatchS3Request(S3Context ctx)
    {
        var req = ctx.Request;

        // Reject expired presigned URLs before any authentication
        if (req.IsPresigned && req.IsExpired)
            throw new S3Exception(new Error(ErrorCode.ExpiredToken));

        // Authenticate and populate ctx.Metadata before any handler reads it
        var md = _Auth.AuthenticateAndBuildMetadata(ctx);

        // Run the appropriate authorization check based on request type
        if (req.RequestType == S3RequestType.ListBuckets || req.RequestType == S3RequestType.ServiceExists)
            md = _Auth.AuthorizeServiceRequest(ctx, md);
        else if (req.IsObjectRequest)
            md = _Auth.AuthorizeObjectRequest(ctx, md);
        else
            md = _Auth.AuthorizeBucketRequest(ctx, md);

        ctx.Metadata = md;

        switch (req.RequestType)
        {
            // ── Service ──────────────────────────────────────────────────────────
            case S3RequestType.ServiceExists:
            {
                string region = await _ApiHandler.ServiceExists(ctx);
                ctx.Response.StatusCode = 200;
                ctx.Response.Headers[Constants.HeaderBucketRegion] = region;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.ListBuckets:
            {
                var result = await _ApiHandler.ServiceListBuckets(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }

            // ── Bucket ───────────────────────────────────────────────────────────
            case S3RequestType.BucketExists:
            {
                bool exists = await _ApiHandler.BucketExists(ctx);
                ctx.Response.StatusCode = exists ? 200 : 404;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.BucketRead:
            {
                if (await ServeWebsite(ctx)) break;
                var result = await _ApiHandler.BucketRead(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.BucketReadAcl:
            {
                var result = await _ApiHandler.BucketReadAcl(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.BucketReadLocation:
            {
                var result = await _ApiHandler.BucketReadLocation(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.BucketReadTags:
            {
                var result = await _ApiHandler.BucketReadTagging(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.BucketReadVersions:
            {
                var result = await _ApiHandler.BucketReadVersions(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.BucketReadVersioning:
            {
                var result = await _ApiHandler.BucketReadVersioning(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.BucketReadMultipartUploads:
            {
                var result = await _ApiHandler.ReadMultipartUploads(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.BucketWrite:
            {
                await _ApiHandler.BucketWrite(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.BucketWriteAcl:
            {
                var acp = SerializationHelper.DeserializeXml<AccessControlPolicy>(ctx.Request.DataAsString);
                await _ApiHandler.BucketWriteAcl(ctx, acp!);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.BucketWriteTags:
            {
                var tagging = SerializationHelper.DeserializeXml<Tagging>(ctx.Request.DataAsString);
                await _ApiHandler.BucketWriteTagging(ctx, tagging!);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.BucketWriteVersioning:
            {
                var versioning = SerializationHelper.DeserializeXml<VersioningConfiguration>(ctx.Request.DataAsString);
                await _ApiHandler.BucketWriteVersioning(ctx, versioning!);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.BucketDelete:
            {
                await _ApiHandler.BucketDelete(ctx);
                ctx.Response.StatusCode = 204;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.BucketDeleteTags:
            {
                await _ApiHandler.BucketDeleteTagging(ctx);
                ctx.Response.StatusCode = 204;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.BucketReadWebsite:
            {
                var result = await _ApiHandler.BucketReadWebsite(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.BucketWriteWebsite:
            {
                var config = SerializationHelper.DeserializeXml<WebsiteConfiguration>(ctx.Request.DataAsString);
                await _ApiHandler.BucketWriteWebsite(ctx, config ?? new WebsiteConfiguration());
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.BucketDeleteWebsite:
            {
                await _ApiHandler.BucketDeleteWebsite(ctx);
                ctx.Response.StatusCode = 204;
                await ctx.Response.Send();
                break;
            }

            // ── Object ───────────────────────────────────────────────────────────
            case S3RequestType.ObjectExists:
            {
                var meta = await _ApiHandler.ObjectExists(ctx);
                if (meta != null)
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Headers[Constants.HeaderETag] = meta.ETag ?? string.Empty;
                    ctx.Response.Headers["Content-Length"] = meta.Size.ToString();
                    ctx.Response.Headers["Content-Type"] = meta.ContentType ?? Constants.ContentTypeOctetStream;
                    ctx.Response.Headers["Last-Modified"] = meta.LastModified.ToString("R"); // RFC 1123
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.ObjectRead:
            {
                if (await ServeWebsite(ctx)) break;
                var obj = await _ApiHandler.ObjectRead(ctx);
                if (obj != null)
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = obj.ContentType ?? Constants.ContentTypeOctetStream;
                    ctx.Response.Headers[Constants.HeaderETag] = obj.ETag ?? string.Empty;
                    ctx.Response.Headers["Content-Length"] = obj.Size.ToString();
                    await ctx.Response.Send(obj.Size, obj.Data!);
                }
                else
                {
                    await ctx.Response.Send(ErrorCode.NoSuchKey);
                }
                break;
            }
            case S3RequestType.ObjectReadRange:
            {
                var obj = await _ApiHandler.ObjectReadRange(ctx);
                if (obj != null)
                {
                    ctx.Response.StatusCode = 206;
                    ctx.Response.ContentType = obj.ContentType ?? Constants.ContentTypeOctetStream;
                    ctx.Response.Headers[Constants.HeaderETag] = obj.ETag ?? string.Empty;
                    ctx.Response.Headers["Content-Length"] = obj.Size.ToString();
                    await ctx.Response.Send(obj.Size, obj.Data!);
                }
                else
                {
                    await ctx.Response.Send(ErrorCode.NoSuchKey);
                }
                break;
            }
            case S3RequestType.ObjectReadAcl:
            {
                var acp = await _ApiHandler.ObjectReadAcl(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(acp));
                break;
            }
            case S3RequestType.ObjectReadTags:
            {
                var tagging = await _ApiHandler.ObjectReadTagging(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(tagging));
                break;
            }
            case S3RequestType.ObjectWrite:
            {
                if (ctx.Http.Request.Headers.ContainsKey("x-amz-copy-source"))
                {
                    // Server-side copy (CopyObject): PUT with x-amz-copy-source header
                    var copyResult = await _ApiHandler.ObjectCopy(ctx);
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.Send(SerializationHelper.SerializeXml(copyResult));
                }
                else
                {
                    await _ApiHandler.ObjectWrite(ctx);
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.Send();
                }
                break;
            }
            case S3RequestType.ObjectWriteAcl:
            {
                var acp = SerializationHelper.DeserializeXml<AccessControlPolicy>(ctx.Request.DataAsString);
                await _ApiHandler.ObjectWriteAcl(ctx, acp!);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.ObjectWriteTags:
            {
                var tagging = SerializationHelper.DeserializeXml<Tagging>(ctx.Request.DataAsString);
                await _ApiHandler.ObjectWriteTagging(ctx, tagging!);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.ObjectDelete:
            {
                await _ApiHandler.ObjectDelete(ctx);
                ctx.Response.StatusCode = 204;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.ObjectDeleteMultiple:
            {
                var dm = SerializationHelper.DeserializeXml<DeleteMultiple>(ctx.Request.DataAsString);
                var result = await _ApiHandler.ObjectDeleteMultiple(ctx, dm!);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.ObjectDeleteTags:
            {
                await _ApiHandler.ObjectDeleteTagging(ctx);
                ctx.Response.StatusCode = 204;
                await ctx.Response.Send();
                break;
            }

            // ── Multipart Upload ──────────────────────────────────────────────────
            case S3RequestType.ObjectCreateMultipartUpload:
            {
                var result = await _ApiHandler.CreateMultipartUpload(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.ObjectUploadPart:
            {
                await _ApiHandler.UploadPart(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.ObjectCompleteMultipartUpload:
            {
                var upload = SerializationHelper.DeserializeXml<CompleteMultipartUpload>(ctx.Request.DataAsString);
                var result = await _ApiHandler.CompleteMultipartUpload(ctx, upload!);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }
            case S3RequestType.ObjectAbortMultipartUpload:
            {
                await _ApiHandler.AbortMultipartUpload(ctx);
                ctx.Response.StatusCode = 204;
                await ctx.Response.Send();
                break;
            }
            case S3RequestType.ObjectReadParts:
            {
                var result = await _ApiHandler.ReadParts(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send(SerializationHelper.SerializeXml(result));
                break;
            }

            default:
                await ctx.Response.Send(ErrorCode.InvalidRequest);
                break;
        }
    }

    private async Task<bool> ServeWebsite(S3Context ctx)
    {
        if (ctx.Request.RequestType != S3RequestType.BucketRead &&
            ctx.Request.RequestType != S3RequestType.ObjectRead &&
            ctx.Request.RequestType != S3RequestType.ObjectExists)
            return false;

        if (string.IsNullOrEmpty(ctx.Request.Bucket)) return false;

        string configPath = System.IO.Path.Combine(_Settings.Storage.DiskDirectory, ctx.Request.Bucket, "website.xml");
        if (!System.IO.File.Exists(configPath)) return false;

        string xml = await System.IO.File.ReadAllTextAsync(configPath);
        var config = SerializationHelper.DeserializeXml<WebsiteConfiguration>(xml);
        if (config == null) return false;

        if (config.RedirectAllRequestsTo != null && !string.IsNullOrEmpty(config.RedirectAllRequestsTo.HostName))
        {
            string protocol = config.RedirectAllRequestsTo.Protocol == ProtocolEnum.Https ? "https" : "http";
            string location = $"{protocol}://{config.RedirectAllRequestsTo.HostName}{ctx.Http.Request.Path}";
            ctx.Response.StatusCode = 301;
            ctx.Response.Headers["Location"] = location;
            await ctx.Response.Send();
            return true;
        }

        string key = ctx.Request.Key ?? string.Empty;
        string indexSuffix = config.IndexDocument?.Suffix ?? "index.html";
        if (string.IsNullOrEmpty(key) || key.EndsWith("/"))
            key = key + indexSuffix;

        if (config.RoutingRules?.Rules != null)
        {
            foreach (var rule in config.RoutingRules.Rules)
            {
                if (rule?.Redirect == null) continue;
                var cond = rule.Condition;
                bool matches = cond == null
                    || (!string.IsNullOrEmpty(cond.KeyPrefixEquals) && key.StartsWith(cond.KeyPrefixEquals));
                if (!matches) continue;

                string redirectKey = rule.Redirect.ReplaceKeyWith ?? rule.Redirect.ReplaceKeyPrefixWith ?? key;
                if (!string.IsNullOrEmpty(rule.Redirect.ReplaceKeyPrefixWith) && cond?.KeyPrefixEquals != null)
                    redirectKey = rule.Redirect.ReplaceKeyPrefixWith + key[cond.KeyPrefixEquals.Length..];

                string redirectHost = !string.IsNullOrEmpty(rule.Redirect.HostName)
                    ? rule.Redirect.HostName
                    : ctx.Request.RetrieveHeaderValue("host") ?? string.Empty;
                string redirectProto = rule.Redirect.Protocol == ProtocolEnum.Https ? "https" : "http";
                ctx.Response.StatusCode = rule.Redirect.HttpRedirectCode;
                ctx.Response.Headers["Location"] = $"{redirectProto}://{redirectHost}/{redirectKey}";
                await ctx.Response.Send();
                return true;
            }
        }

        var bucket = _Config.GetBucketByName(ctx.Request.Bucket);
        if (bucket == null) return false;
        var client = _Buckets.GetClient(ctx.Request.Bucket);
        if (client == null) return false;

        var objMeta = client.GetObjectLatestMetadata(key);

        if (objMeta == null)
        {
            string? errorKey = config.ErrorDocument?.Key;
            if (!string.IsNullOrEmpty(errorKey))
            {
                var errorMeta = client.GetObjectLatestMetadata(errorKey);
                if (errorMeta != null)
                {
                    string errorBlobPath = bucket.DiskDirectory + errorMeta.BlobFilename;
                    byte[] errorData = System.IO.File.Exists(errorBlobPath)
                        ? await System.IO.File.ReadAllBytesAsync(errorBlobPath)
                        : Array.Empty<byte>();
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Headers["Content-Type"] = errorMeta.ContentType ?? "text/html";
                    await ctx.Response.Send(errorData);
                    return true;
                }
            }
            ctx.Response.StatusCode = 404;
            ctx.Response.Headers["Content-Type"] = "text/html";
            await ctx.Response.Send("<html><body><h1>404 Not Found</h1></body></html>");
            return true;
        }

        string blobPath = bucket.DiskDirectory + objMeta.BlobFilename;
        byte[] data = System.IO.File.Exists(blobPath)
            ? await System.IO.File.ReadAllBytesAsync(blobPath)
            : Array.Empty<byte>();
        ctx.Response.StatusCode = 200;
        ctx.Response.Headers["Content-Type"] = objMeta.ContentType ?? GetMimeType(key);
        ctx.Response.Headers["ETag"] = objMeta.Etag ?? string.Empty;
        ctx.Response.Headers["Last-Modified"] = objMeta.LastUpdateUtc.ToString("R");
        await ctx.Response.Send(data);
        return true;
    }

    private static string GetMimeType(string key)
    {
        return System.IO.Path.GetExtension(key).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".txt" => "text/plain",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }

    #endregion
}
