using System.Net;
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
    private Socket? _listener;
    private CancellationTokenSource? _cts;

    public async Task StartAsync(
        int port,
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxRequestBodySize = 64 * 1024 * 1024,
        string? certPath = null,
        string? certPassword = null,
        bool enableHttp2 = false,
        CancellationToken cancellationToken = default)
    {
#pragma warning disable SYSLIB0057
        X509Certificate2? cert = certPath is not null
            ? new X509Certificate2(certPath, certPassword)
            : null;
#pragma warning restore SYSLIB0057

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        _listener.DualMode = true;  // accept both IPv4 and IPv6
        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        _listener.Listen(backlog: 512);

        var scheme = cert is not null ? "https" : "http";
        Console.WriteLine($"CosmoApiServer listening on {scheme}://0.0.0.0:{port}");

        // Accept loop runs in background — fire and forget (bounded by OS)
        _ = AcceptLoopAsync(pipeline, services, maxRequestBodySize, cert, enableHttp2, _cts.Token);
    }

    private async ValueTask AcceptLoopAsync(
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxBodySize,
        X509Certificate2? cert,
        bool enableHttp2,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try { client = await _listener!.AcceptAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch { continue; } // transient accept errors

            // Handle each connection independently; don't await (keep accepting)
            _ = HandleConnectionAsync(client, pipeline, services, maxBodySize, cert, enableHttp2, ct);
        }
    }

    private static async ValueTask HandleConnectionAsync(
        Socket socket,
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxBodySize,
        X509Certificate2? cert,
        bool enableHttp2,
        CancellationToken ct)
    {
        socket.NoDelay = true;
        Stream stream = new NetworkStream(socket, ownsSocket: true);

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
                await ssl.AuthenticateAsServerAsync(sslOptions, ct);
                stream = ssl;

                if (enableHttp2 && ssl.NegotiatedApplicationProtocol == SslApplicationProtocol.Http2)
                {
                    await Http2Connection.RunAsync(ssl, pipeline, services, ct);
                    return;
                }
            }

            // HTTP/1.1 (with optional h2c upgrade detection inside)
            await Http11Connection.RunAsync(stream, pipeline, services, maxBodySize, enableHttp2, ct);
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

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Close();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _listener?.Dispose();
        _cts?.Dispose();
    }
}
