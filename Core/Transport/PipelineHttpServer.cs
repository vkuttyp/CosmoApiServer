using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using CosmoApiServer.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// TCP listener built on <see cref="Socket"/> + <see cref="System.IO.Pipelines"/>.
/// Replaces the DotNetty <c>HttpServerChannel</c> with zero-copy I/O and no
/// EventLoop → ThreadPool context switch on the hot path.
/// </summary>
public sealed class PipelineHttpServer : IAsyncDisposable
{
    private const long Http3NoError = 0x0100;
    private Socket? _listener;
    private QuicListener? _quicListener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<Socket, byte> _activeSockets = new();
#pragma warning disable CA1416
    private readonly ConcurrentDictionary<QuicConnection, byte> _activeQuicConnections = new();
#pragma warning restore CA1416
    private static readonly SslApplicationProtocol Http3Protocol = new("h3");

    // Certificate hot-reload: new connections always read from this volatile field.
#pragma warning disable CA1416
    private volatile SslStreamCertificateContext? _quicCertContext;
#pragma warning restore CA1416
    private FileSystemWatcher? _certWatcher;
    private string? _certPath;
    private string? _certPassword;
    private ILogger? _logger;

    private static bool SupportsQuicPlatform() =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    private Socket? _httpsListener;
    private Func<string?, X509Certificate2?>? _certSelector;
    private Func<string?, SslStreamCertificateContext?>? _certContextSelector;

    public async Task StartAsync(
        int port,
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxRequestBodySize = 64 * 1024 * 1024,
        string? certPath = null,
        string? certPassword = null,
        bool enableHttp2 = false,
        bool enableHttp3 = false,
        int connectionTimeoutSeconds = 120,
        int http3MaxRequestsPerConnection = 100,
        int http3MaxConcurrentStreams = 100,
        int http3IdleTimeoutSeconds = 30,
        int http3MaxUnidirectionalStreams = 10,
        int http3MaxFieldSectionSize = 16 * 1024,
        CancellationToken cancellationToken = default,
        Func<string?, X509Certificate2?>? certificateSelector = null,
        int httpsPort = 0,
        Func<string?, SslStreamCertificateContext?>? certificateContextSelector = null)
    {
        _logger = services.GetService<ILoggerFactory>()?.CreateLogger("CosmoApiServer");
        _certPath = certPath;
        _certPassword = certPassword;
        _certSelector = certificateSelector;
        _certContextSelector = certificateContextSelector;

#pragma warning disable SYSLIB0057
        X509Certificate2? cert = certPath is not null
            ? new X509Certificate2(certPath, certPassword)
            : null;
#pragma warning restore SYSLIB0057

        if (cert is not null && enableHttp3)
        {
#pragma warning disable CA1416
            _quicCertContext = SslStreamCertificateContext.Create(cert, additionalCertificates: null);
#pragma warning restore CA1416
            SetupCertificateHotReload(certPath!);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        _listener.DualMode = true;  // accept both IPv4 and IPv6
        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        _listener.Listen(backlog: 512);

        var scheme = cert is not null ? "https" : "http";
        var startMsg = $"CosmoApiServer listening on {scheme}://0.0.0.0:{port}";
        _logger?.LogInformation(startMsg);
        if (_logger is null) Console.WriteLine(startMsg);

        // QUIC needs a cert; resolve into a separate local so we don't pollute
        // the cleartext-vs-TLS detection below (the primary `cert` is null when
        // only an SNI selector is configured, and the cleartext listener uses
        // `cert is not null` as its TLS gate).
        X509Certificate2? quicCert = cert;
        if (enableHttp3)
        {
            if (quicCert is null && certificateContextSelector is not null)
                quicCert = certificateContextSelector(null)?.TargetCertificate;
            if (quicCert is null && certificateSelector is not null)
                quicCert = certificateSelector(null);

            if (quicCert is null)
            {
                var msg = "HTTP/3 disabled: no TLS certificate available. Falling back to HTTP/2.";
                _logger?.LogWarning(msg);
                if (_logger is null) Console.WriteLine($"[warn] {msg}");
                enableHttp3 = false;
            }
            else if (!SupportsQuicPlatform() || !QuicListener.IsSupported || !QuicConnection.IsSupported)
            {
                var msg = "HTTP/3 disabled: QUIC not supported (install libmsquic). Falling back to HTTP/2.";
                _logger?.LogWarning(msg);
                if (_logger is null) Console.WriteLine($"[warn] {msg}");
                enableHttp3 = false;
            }
            else if (_quicCertContext is null)
            {
#pragma warning disable CA1416
                _quicCertContext = SslStreamCertificateContext.Create(quicCert, additionalCertificates: null);
#pragma warning restore CA1416
            }
        }

        if (enableHttp3)
        {

#pragma warning disable CA1416
            _quicListener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ApplicationProtocols = [Http3Protocol],
                ListenBacklog = 512,
                ListenEndPoint = new IPEndPoint(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, httpsPort > 0 ? httpsPort : port),
                ConnectionOptionsCallback = (_, _, callbackCt) =>
                {
                    var options = new QuicServerConnectionOptions
                    {
                        DefaultCloseErrorCode = Http3NoError,
                        DefaultStreamErrorCode = Http3NoError,
                        IdleTimeout = TimeSpan.FromSeconds(http3IdleTimeoutSeconds),
                        MaxInboundBidirectionalStreams = http3MaxConcurrentStreams,
                        MaxInboundUnidirectionalStreams = http3MaxUnidirectionalStreams,
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            // Read from volatile field so hot-reloaded certs are picked up automatically.
                            ServerCertificateContext = _quicCertContext,
                            ApplicationProtocols = [Http3Protocol],
                            EnabledSslProtocols = SslProtocols.Tls13
                        }
                    };
                    return ValueTask.FromResult(options);
                }
            }, _cts.Token);
#pragma warning restore CA1416

            var h3Msg = $"CosmoApiServer HTTP/3 listener on https://0.0.0.0:{(httpsPort > 0 ? httpsPort : port)} (UDP/QUIC)";
            _logger?.LogInformation(h3Msg);
            if (_logger is null) Console.WriteLine(h3Msg);
            _ = AcceptQuicLoopAsync(pipeline, services, connectionTimeoutSeconds, http3MaxRequestsPerConnection, http3MaxFieldSectionSize, _cts.Token);
        }

        // Compute Alt-Svc value once; null when HTTP/3 is not enabled.
        string? altSvcValue = enableHttp3 ? $"h3=\":{(httpsPort > 0 ? httpsPort : port)}\"; ma=86400" : null;

        // When a separate HTTPS port is configured, the primary (cleartext) listener
        // should NOT use h2c — HTTP/2 is negotiated via ALPN on the TLS listener only.
        var cleartextHttp2 = httpsPort > 0 ? false : enableHttp2;

        // Accept loop for cleartext HTTP — never uses TLS even if a cert selector exists
        _ = AcceptLoopAsync(pipeline, services, maxRequestBodySize, cert, cleartextHttp2, connectionTimeoutSeconds, altSvcValue, useTls: cert is not null, _cts.Token);

        // Optional second listener for HTTPS on a separate port (SNI-based cert selection)
        if (httpsPort > 0 && (cert is not null || certificateSelector is not null || certificateContextSelector is not null))
        {
            try
            {
                _httpsListener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                _httpsListener.DualMode = true;
                _httpsListener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _httpsListener.Bind(new IPEndPoint(IPAddress.IPv6Any, httpsPort));
                _httpsListener.Listen(backlog: 512);

                var httpsMsg = $"CosmoApiServer HTTPS listening on https://0.0.0.0:{httpsPort}";
                _logger?.LogInformation(httpsMsg);
                if (_logger is null) Console.WriteLine(httpsMsg);

                _ = AcceptLoopAsync(pipeline, services, maxRequestBodySize, cert, enableHttp2, connectionTimeoutSeconds, altSvcValue, useTls: true, _cts.Token, _httpsListener);
            }
            catch (Exception ex)
            {
                var errMsg = $"[HTTPS] Failed to start HTTPS listener on port {httpsPort}: {ex.Message}";
                _logger?.LogError(ex, errMsg);
                if (_logger is null) Console.Error.WriteLine(errMsg);
            }
        }
    }

    private async ValueTask AcceptLoopAsync(
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxBodySize,
        X509Certificate2? cert,
        bool enableHttp2,
        int connectionTimeoutSeconds,
        string? altSvcValue,
        bool useTls,
        CancellationToken ct,
        Socket? overrideListener = null)
    {
        var listener = overrideListener ?? _listener!;
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try { client = await listener.AcceptAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch { continue; } // transient accept errors

            _ = HandleConnectionAsync(client, pipeline, services, maxBodySize, cert, enableHttp2, connectionTimeoutSeconds, altSvcValue, useTls, ct);
        }
    }

    private async ValueTask AcceptQuicLoopAsync(
        RequestDelegate pipeline,
        IServiceProvider services,
        int connectionTimeoutSeconds,
        int http3MaxRequestsPerConnection,
        int http3MaxFieldSectionSize,
        CancellationToken ct)
    {
#pragma warning disable CA1416
        while (!ct.IsCancellationRequested)
        {
            QuicConnection connection;
            try
            {
                connection = await _quicListener!.AcceptConnectionAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[HTTP/3 Accept] {ExType}", ex.GetType().Name);
                if (_logger is null) Console.Error.WriteLine($"[HTTP/3 Accept] {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            _ = HandleQuicConnectionAsync(connection, pipeline, services, connectionTimeoutSeconds, http3MaxRequestsPerConnection, http3MaxFieldSectionSize, ct);
        }
#pragma warning restore CA1416
    }

    private async ValueTask HandleConnectionAsync(
        Socket socket,
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxBodySize,
        X509Certificate2? cert,
        bool enableHttp2,
        int connectionTimeoutSeconds,
        string? altSvcValue,
        bool useTls,
        CancellationToken ct)
    {
        _activeSockets.TryAdd(socket, 0);
        socket.NoDelay = true;
        var remoteIp = (socket.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "unknown";
        Stream stream = new NetworkStream(socket, ownsSocket: true);

        // Per-connection timeout — prevents slowloris and idle connection exhaustion
        using var connectionCts = connectionTimeoutSeconds > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        connectionCts?.CancelAfter(TimeSpan.FromSeconds(connectionTimeoutSeconds));
        var connectionCt = connectionCts?.Token ?? ct;

        try
        {
            if (useTls)
            {
                // TLS: negotiate protocol via ALPN
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                var sslOptions = new SslServerAuthenticationOptions
                {
                    ClientCertificateRequired = false,
                    ApplicationProtocols   = enableHttp2
                        ? [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11]
                        : [SslApplicationProtocol.Http11],
                };

                if (_certContextSelector is not null)
                {
                    // Workaround for .NET 10 bug: ServerCertificateSelectionCallback can't use
                    // PEM-loaded certs. Use ServerCertificateContext only (no callback).
                    // See: https://github.com/dotnet/runtime/issues/127207
                    //
                    // Peek at TLS ClientHello via Socket.Receive(MSG_PEEK) to extract SNI
                    // without consuming bytes, then select the right SslStreamCertificateContext.
                    var sni = PeekSniFromSocket(socket);
                    sslOptions.ServerCertificateContext = _certContextSelector(sni);
                }
                else if (_certSelector is not null)
                {
                    var selector = _certSelector;
                    sslOptions.ServerCertificateSelectionCallback = (_, hostname) => selector(hostname);
                }
                else
                {
                    sslOptions.ServerCertificate = cert;
                }

                await ssl.AuthenticateAsServerAsync(sslOptions, connectionCt);
                stream = ssl;

                if (enableHttp2 && ssl.NegotiatedApplicationProtocol == SslApplicationProtocol.Http2)
                {
                    await Http2Connection.RunAsync(ssl, pipeline, services, connectionCt, altSvcValue, isHttps: true);
                    return;
                }
            }

            // HTTP/1.1 (with optional h2c upgrade detection inside)
            await Http11Connection.RunAsync(stream, pipeline, services, maxBodySize, enableHttp2, remoteIp, connectionCt, altSvcValue, isHttps: useTls);
        }
        catch (AuthenticationException ex)
        {
            _logger?.LogWarning(ex, "[TLS] {Message}", ex.Message);
            if (_logger is null) Console.Error.WriteLine($"[TLS] {ex.Message}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SocketException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Connection] {ExType}", ex.GetType().Name);
            if (_logger is null) Console.Error.WriteLine($"[Connection] {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _activeSockets.TryRemove(socket, out _);
            await stream.DisposeAsync();
        }
    }

    private async ValueTask HandleQuicConnectionAsync(
        QuicConnection connection,
        RequestDelegate pipeline,
        IServiceProvider services,
        int connectionTimeoutSeconds,
        int http3MaxRequestsPerConnection,
        int http3MaxFieldSectionSize,
        CancellationToken ct)
    {
#pragma warning disable CA1416
        _activeQuicConnections.TryAdd(connection, 0);
        using var connectionCts = connectionTimeoutSeconds > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        connectionCts?.CancelAfter(TimeSpan.FromSeconds(connectionTimeoutSeconds));
        var connectionCt = connectionCts?.Token ?? ct;

        try
        {
            await Http3Connection.RunAsync(connection, pipeline, services, http3MaxRequestsPerConnection, connectionCt, http3MaxFieldSectionSize);
        }
        catch (OperationCanceledException) { }
        catch (QuicException ex)
        {
            _logger?.LogError(ex, "[HTTP/3] {Message}", ex.Message);
            if (_logger is null) Console.Error.WriteLine($"[HTTP/3] {ex.Message}");
        }
        finally
        {
            _activeQuicConnections.TryRemove(connection, out _);
            await connection.DisposeAsync();
        }
#pragma warning restore CA1416
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Close();
        foreach (var socket in _activeSockets.Keys)
        {
            try { socket.Dispose(); } catch { }
        }
        if (SupportsQuicPlatform())
        {
#pragma warning disable CA1416
            foreach (var connection in _activeQuicConnections.Keys)
            {
                try { connection.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            }
            _quicListener?.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore CA1416
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _certWatcher?.Dispose();
        _listener?.Dispose();
        _httpsListener?.Dispose();
        _cts?.Dispose();
    }

    /// <summary>
    /// Peeks at the TLS ClientHello via Socket.Receive(MSG_PEEK) to extract the SNI hostname
    /// without consuming bytes. Returns null if SNI cannot be extracted.
    /// </summary>
    private static string? PeekSniFromSocket(Socket socket)
    {
        try
        {
            var buf = new byte[4096];
            var len = socket.Receive(buf, 0, buf.Length, SocketFlags.Peek);
            if (len < 43) return null; // Too short for a ClientHello

            // TLS record: type(1) + version(2) + length(2) + handshake
            if (buf[0] != 0x16) return null; // Not a TLS Handshake
            int recordLen = (buf[3] << 8) | buf[4];
            if (len < 5 + recordLen) { /* partial, continue with what we have */ }

            // Handshake: type(1) + length(3) + clientVersion(2) + random(32) = offset 43
            if (buf[5] != 0x01) return null; // Not ClientHello
            int pos = 43; // skip to session ID

            if (pos >= len) return null;
            int sessionIdLen = buf[pos]; pos += 1 + sessionIdLen; // skip session ID
            if (pos + 2 > len) return null;
            int cipherSuitesLen = (buf[pos] << 8) | buf[pos + 1]; pos += 2 + cipherSuitesLen;
            if (pos + 1 > len) return null;
            int compMethodsLen = buf[pos]; pos += 1 + compMethodsLen;

            // Extensions
            if (pos + 2 > len) return null;
            int extensionsLen = (buf[pos] << 8) | buf[pos + 1]; pos += 2;
            int extensionsEnd = pos + extensionsLen;

            while (pos + 4 <= len && pos < extensionsEnd)
            {
                int extType = (buf[pos] << 8) | buf[pos + 1];
                int extLen = (buf[pos + 2] << 8) | buf[pos + 3];
                pos += 4;

                if (extType == 0x0000) // SNI extension
                {
                    if (pos + 5 > len) return null;
                    // SNI list: listLen(2) + type(1) + nameLen(2) + name
                    int nameLen = (buf[pos + 3] << 8) | buf[pos + 4];
                    if (pos + 5 + nameLen > len) return null;
                    return System.Text.Encoding.ASCII.GetString(buf, pos + 5, nameLen);
                }

                pos += extLen;
            }
        }
        catch { }
        return null;
    }

    private void SetupCertificateHotReload(string certFilePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(certFilePath))!;
        var file = Path.GetFileName(certFilePath);

        _certWatcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _certWatcher.Changed += (_, _) => ReloadCertificate();
        _certWatcher.Created += (_, _) => ReloadCertificate();
    }

    private void ReloadCertificate()
    {
        if (_certPath is null) return;
        try
        {
            // Brief delay to allow the file write to complete before reading.
            Thread.Sleep(500);
#pragma warning disable SYSLIB0057
            var newCert = new X509Certificate2(_certPath, _certPassword);
#pragma warning restore SYSLIB0057
#pragma warning disable CA1416
            var newContext = SslStreamCertificateContext.Create(newCert, additionalCertificates: null);
            _quicCertContext = newContext; // volatile write — immediately visible to new connections
#pragma warning restore CA1416
            _logger?.LogInformation("HTTP/3 TLS certificate hot-reloaded from {Path}", _certPath);
            if (_logger is null) Console.WriteLine($"[TLS] Certificate hot-reloaded from {_certPath}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "HTTP/3 TLS certificate hot-reload failed for {Path}", _certPath);
            if (_logger is null) Console.Error.WriteLine($"[TLS] Certificate hot-reload failed: {ex.Message}");
        }
    }
}
