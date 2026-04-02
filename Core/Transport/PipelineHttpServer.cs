using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using CosmoApiServer.Core.Middleware;

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
    private static readonly SslApplicationProtocol Http3Protocol = new("h3");

    private static bool SupportsQuicPlatform() =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

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
        CancellationToken cancellationToken = default)
    {
#pragma warning disable SYSLIB0057
        X509Certificate2? cert = certPath is not null
            ? new X509Certificate2(certPath, certPassword)
            : null;
#pragma warning restore SYSLIB0057
        SslStreamCertificateContext? quicCertContext = cert is not null && enableHttp3
            ? SslStreamCertificateContext.Create(cert, additionalCertificates: null)
            : null;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        _listener.DualMode = true;  // accept both IPv4 and IPv6
        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        _listener.Listen(backlog: 512);

        var scheme = cert is not null ? "https" : "http";
        Console.WriteLine($"CosmoApiServer listening on {scheme}://0.0.0.0:{port}");

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
                                MaxInboundBidirectionalStreams = 100,
                        MaxInboundUnidirectionalStreams = 100,
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            ServerCertificateContext = quicCertContext,
                            ApplicationProtocols = [Http3Protocol],
                            EnabledSslProtocols = SslProtocols.Tls13
                        }
                    };
                    return ValueTask.FromResult(options);
                }
            }, _cts.Token);
#pragma warning restore CA1416

            Console.WriteLine($"CosmoApiServer experimental HTTP/3 listener on https://0.0.0.0:{port}");
            _ = AcceptQuicLoopAsync(pipeline, services, connectionTimeoutSeconds, _cts.Token);
        }

        // Accept loop runs in background — fire and forget (bounded by OS)
        _ = AcceptLoopAsync(pipeline, services, maxRequestBodySize, cert, enableHttp2, connectionTimeoutSeconds, _cts.Token);
    }

    private async ValueTask AcceptLoopAsync(
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxBodySize,
        X509Certificate2? cert,
        bool enableHttp2,
        int connectionTimeoutSeconds,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try { client = await _listener!.AcceptAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch { continue; } // transient accept errors

            // Handle each connection independently; don't await (keep accepting)
            _ = HandleConnectionAsync(client, pipeline, services, maxBodySize, cert, enableHttp2, connectionTimeoutSeconds, ct);
        }
    }

    private async ValueTask AcceptQuicLoopAsync(
        RequestDelegate pipeline,
        IServiceProvider services,
        int connectionTimeoutSeconds,
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
                Console.Error.WriteLine($"[HTTP/3 Accept] {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            _ = HandleQuicConnectionAsync(connection, pipeline, services, connectionTimeoutSeconds, ct);
        }
#pragma warning restore CA1416
    }

    private static async ValueTask HandleConnectionAsync(
        Socket socket,
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxBodySize,
        X509Certificate2? cert,
        bool enableHttp2,
        int connectionTimeoutSeconds,
        CancellationToken ct)
    {
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
            if (cert is not null)
            {
                // TLS: negotiate protocol via ALPN
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                var sslOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate      = cert,
                    ClientCertificateRequired = false,
                    ApplicationProtocols   = enableHttp2
                        ? [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11]
                        : [SslApplicationProtocol.Http11],
                };
                await ssl.AuthenticateAsServerAsync(sslOptions, connectionCt);
                stream = ssl;

                if (enableHttp2 && ssl.NegotiatedApplicationProtocol == SslApplicationProtocol.Http2)
                {
                    await Http2Connection.RunAsync(ssl, pipeline, services, connectionCt);
                    return;
                }
            }

            // HTTP/1.1 (with optional h2c upgrade detection inside)
            await Http11Connection.RunAsync(stream, pipeline, services, maxBodySize, enableHttp2, remoteIp, connectionCt);
        }
        catch (AuthenticationException ex)
        {
            Console.Error.WriteLine($"[TLS] {ex.Message}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SocketException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Connection] {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    private static async ValueTask HandleQuicConnectionAsync(
        QuicConnection connection,
        RequestDelegate pipeline,
        IServiceProvider services,
        int connectionTimeoutSeconds,
        CancellationToken ct)
    {
#pragma warning disable CA1416
        using var connectionCts = connectionTimeoutSeconds > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        connectionCts?.CancelAfter(TimeSpan.FromSeconds(connectionTimeoutSeconds));
        var connectionCt = connectionCts?.Token ?? ct;

        try
        {
            await Http3Connection.RunAsync(connection, pipeline, services, connectionCt);
        }
        catch (OperationCanceledException) { }
        catch (QuicException ex)
        {
            Console.Error.WriteLine($"[HTTP/3] {ex.Message}");
        }
        finally
        {
            await connection.DisposeAsync();
        }
#pragma warning restore CA1416
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Close();
        if (SupportsQuicPlatform())
        {
#pragma warning disable CA1416
            _quicListener?.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore CA1416
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _listener?.Dispose();
        _cts?.Dispose();
    }
}
