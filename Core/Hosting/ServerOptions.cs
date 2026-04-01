namespace CosmoApiServer.Core.Hosting;

public sealed class ServerOptions
{
    public int Port { get; set; } = 8080;
    public int MaxRequestBodySize { get; set; } = 30 * 1024 * 1024; // 30 MB

    // TLS / HTTPS
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// When true, advertise HTTP/2 (h2) via ALPN alongside HTTP/1.1.
    /// Requires <see cref="EnableTls"/> to be true.
    /// </summary>
    public bool EnableHttp2 { get; set; } = false;

    /// <summary>
    /// When true, start an experimental HTTP/3 QUIC listener on the same port.
    /// Requires TLS and runtime QUIC support.
    /// </summary>
    public bool EnableHttp3 { get; set; } = false;

    /// <summary>True when a certificate path has been configured.</summary>
    public bool EnableTls => CertificatePath is not null;

    /// <summary>
    /// Maximum lifetime of a single HTTP/1.1 connection in seconds.
    /// Prevents slowloris and idle connection exhaustion. Default 120s.
    /// Set to 0 to disable (not recommended in production).
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 120;
}
