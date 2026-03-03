namespace CosmoApiServer.Core.Hosting;

public sealed class ServerOptions
{
    public int Port { get; set; } = 8080;
    public int MaxRequestBodySize { get; set; } = 1024 * 1024 * 1024; // 1 GB

    // TLS / HTTPS
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// When true, advertise HTTP/2 (h2) via ALPN alongside HTTP/1.1.
    /// Requires <see cref="EnableTls"/> to be true.
    /// </summary>
    public bool EnableHttp2 { get; set; } = false;

    /// <summary>True when a certificate path has been configured.</summary>
    public bool EnableTls => CertificatePath is not null;
}
