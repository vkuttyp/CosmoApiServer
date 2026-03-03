namespace CosmoS3.Settings;

/// <summary>
/// CORS configuration for CosmoS3.
/// </summary>
public sealed class CorsSettings
{
    /// <summary>
    /// Whether CORS headers should be emitted.  Default: <c>false</c>.
    /// Set to <c>true</c> when browser-based S3 clients (e.g. the AWS SDK for
    /// JavaScript) will be making cross-origin requests.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Origins that are allowed to make cross-origin requests.
    /// Use <c>["*"]</c> for any origin (development only).
    /// Default: <c>["*"]</c>.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = ["*"];

    /// <summary>
    /// HTTP methods that are allowed in CORS requests.
    /// Default covers all standard S3 operations.
    /// </summary>
    public string[] AllowedMethods { get; set; } =
        ["GET", "PUT", "POST", "DELETE", "HEAD", "OPTIONS"];

    /// <summary>
    /// Request headers that the browser is allowed to send.
    /// Default allows the headers required by AWS SigV4.
    /// </summary>
    public string[] AllowedHeaders { get; set; } =
        ["Content-Type", "Authorization", "x-amz-date", "x-amz-content-sha256",
         "x-amz-security-token", "x-amz-acl", "x-amz-storage-class", "ETag"];
}
