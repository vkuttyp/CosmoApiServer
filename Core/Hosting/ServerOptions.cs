namespace CosmoApiServer.Core.Hosting;

public sealed class ServerOptions
{
    public int Port { get; set; } = 8080;
    public int MaxRequestBodySize { get; set; } = 30 * 1024 * 1024; // 30 MB

    // TLS / HTTPS
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Optional SNI callback for selecting a certificate per hostname.
    /// When set, takes precedence over <see cref="CertificatePath"/>.
    /// </summary>
    public Func<string?, System.Security.Cryptography.X509Certificates.X509Certificate2?>? CertificateSelector { get; set; }

    /// <summary>
    /// When set to a non-zero value, starts a second TLS listener on this port.
    /// The primary listener on <see cref="Port"/> remains cleartext.
    /// </summary>
    public int HttpsPort { get; set; }

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

    /// <summary>True when a certificate path or SNI selector has been configured.</summary>
    public bool EnableTls => CertificatePath is not null || CertificateSelector is not null;

    /// <summary>
    /// Maximum lifetime of a single HTTP/1.1 connection in seconds.
    /// Prevents slowloris and idle connection exhaustion. Default 120s.
    /// Set to 0 to disable (not recommended in production).
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum number of requests served on a single HTTP/3 QUIC connection before
    /// sending GOAWAY and requiring the client to open a new connection.
    /// Default 100. Set to 0 to disable the per-connection limit.
    /// </summary>
    public int Http3MaxRequestsPerConnection { get; set; } = 100;

    /// <summary>
    /// Maximum number of simultaneous inbound HTTP/3 QUIC streams (i.e. concurrent requests)
    /// per connection. Clients that exceed this limit receive QUIC stream errors.
    /// Default 100.
    /// </summary>
    public int Http3MaxConcurrentStreams { get; set; } = 100;

    /// <summary>
    /// QUIC connection idle timeout in seconds. Connections with no activity for longer
    /// than this duration are closed by the server. Default 30s.
    /// </summary>
    public int Http3IdleTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of inbound unidirectional QUIC streams per HTTP/3 connection.
    /// The HTTP/3 spec requires at least 3 (control + two QPACK streams). Default 10.
    /// </summary>
    public int Http3MaxUnidirectionalStreams { get; set; } = 10;

    /// <summary>
    /// Maximum size of the HTTP/3 field section (headers + trailers) in bytes, sent to
    /// clients in the SETTINGS frame. Protects against header-bombing attacks. Default 16 KB.
    /// </summary>
    public int Http3MaxFieldSectionSize { get; set; } = 16 * 1024;
}
