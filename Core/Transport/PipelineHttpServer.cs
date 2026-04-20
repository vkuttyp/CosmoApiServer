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
        int httpsPort = 0)
    {
        _logger = services.GetService<ILoggerFactory>()?.CreateLogger("CosmoApiServer");
        _certPath = certPath;
        _certPassword = certPassword;
        _certSelector = certificateSelector;

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

        if (enableHttp3)
        {
            if (cert is null)
                throw new InvalidOperationException("HTTP/3 requires TLS. Configure UseHttps(...) before UseHttp3().");

            if (!SupportsQuicPlatform() || !QuicListener.IsSupported || !QuicConnection.IsSupported)
                throw new PlatformNotSupportedException("HTTP/3 requires runtime QUIC support on this platform.");

#pragma warning disable CA1416
            _quicListener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ApplicationProtocols = [Http3Protocol],
                ListenBacklog = 512,
                ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, port),
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

            var h3Msg = $"CosmoApiServer experimental HTTP/3 listener on https://0.0.0.0:{port}";
            _logger?.LogInformation(h3Msg);
            if (_logger is null) Console.WriteLine(h3Msg);
            _ = AcceptQuicLoopAsync(pipeline, services, connectionTimeoutSeconds, http3MaxRequestsPerConnection, http3MaxFieldSectionSize, _cts.Token);
        }

        // Compute Alt-Svc value once; null when HTTP/3 is not enabled.
        string? altSvcValue = enableHttp3 ? $"h3=\":{port}\"; ma=86400" : null;

        // Accept loop runs in background — fire and forget (bounded by OS)
        _ = AcceptLoopAsync(pipeline, services, maxRequestBodySize, cert, enableHttp2, connectionTimeoutSeconds, altSvcValue, _cts.Token);

        // Optional second listener for HTTPS on a separate port (SNI-based cert selection)
        if (httpsPort > 0 && (cert is not null || certificateSelector is not null))
        {
            _httpsListener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            _httpsListener.DualMode = true;
            _httpsListener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _httpsListener.Bind(new IPEndPoint(IPAddress.IPv6Any, httpsPort));
            _httpsListener.Listen(backlog: 512);

            var httpsMsg = $"CosmoApiServer HTTPS listening on https://0.0.0.0:{httpsPort}";
            _logger?.LogInformation(httpsMsg);
            if (_logger is null) Console.WriteLine(httpsMsg);

            _ = AcceptLoopAsync(pipeline, services, maxRequestBodySize, cert, enableHttp2, connectionTimeoutSeconds, altSvcValue, _cts.Token, _httpsListener);
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

            // Handle each connection independently; don't await (keep accepting)
            _ = HandleConnectionAsync(client, pipeline, services, maxBodySize, cert, enableHttp2, connectionTimeoutSeconds, altSvcValue, ct);
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
            if (cert is not null || _certSelector is not null)
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

                if (_certSelector is not null)
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
                    await Http2Connection.RunAsync(ssl, pipeline, services, connectionCt, altSvcValue);
                    return;
                }
            }

            // HTTP/1.1 (with optional h2c upgrade detection inside)
            await Http11Connection.RunAsync(stream, pipeline, services, maxBodySize, enableHttp2, remoteIp, connectionCt, altSvcValue);
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
