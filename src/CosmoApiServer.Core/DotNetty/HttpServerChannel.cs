using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using CosmoApiServer.Core.Middleware;
using DotNetty.Codecs.Http;
using DotNetty.Handlers.Tls;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace CosmoApiServer.Core.DotNetty;

public sealed class HttpServerChannel : IAsyncDisposable
{
    private readonly IEventLoopGroup _bossGroup;
    private readonly IEventLoopGroup _workerGroup;
    private IChannel? _channel;

    public HttpServerChannel()
    {
        _bossGroup = new MultithreadEventLoopGroup(1);
        _workerGroup = new MultithreadEventLoopGroup();
    }

    public async Task StartAsync(
        int port,
        RequestDelegate pipeline,
        IServiceProvider services,
        int maxRequestBodySize = 64 * 1024 * 1024, // 64 MB default
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

        var bootstrap = new ServerBootstrap()
            .Group(_bossGroup, _workerGroup)
            .Channel<TcpServerSocketChannel>()
            .Option(ChannelOption.SoBacklog, 128)
            .ChildOption(ChannelOption.TcpNodelay, true)
            .ChildHandler(new ActionChannelInitializer<ISocketChannel>(channel =>
            {
                var p = channel.Pipeline;

                if (cert is not null)
                {
                    // TLS mode: HTTP/1.1 over TLS.
                    // ALPN (h2) requires SpanNetty – DotNetty 0.7.6 exposes TLS only.
                    var tlsSettings = new ServerTlsSettings(cert);
                    p.AddLast("tls", new TlsHandler(tlsSettings));
                    AddHttp11Handlers(p, pipeline, services, maxRequestBodySize);
                }
                else if (enableHttp2)
                {
                    // h2c (HTTP/2 cleartext): detect the connection preface and
                    // route to the appropriate codec.
                    p.AddLast("h2c-detect", new Http2PrefaceHandler(pipeline, services, maxRequestBodySize));
                }
                else
                {
                    AddHttp11Handlers(p, pipeline, services, maxRequestBodySize);
                }
            }));

        _channel = await bootstrap.BindAsync(port);
        var scheme = cert is not null ? "https" : "http";
        Console.WriteLine($"CosmoApiServer listening on {scheme}://0.0.0.0:{port}");
    }

    internal static void AddHttp11Handlers(
        IChannelPipeline pipeline,
        RequestDelegate appPipeline,
        IServiceProvider services,
        int maxRequestBodySize)
    {
        pipeline.AddLast("http-decoder",    new HttpRequestDecoder());
        pipeline.AddLast("http-encoder",    new HttpResponseEncoder());
        pipeline.AddLast("http-aggregator", new HttpObjectAggregator(maxRequestBodySize));
        pipeline.AddLast("http-handler",    new HttpChannelHandler(appPipeline, services));
    }

    public async Task StopAsync()
    {
        if (_channel is not null)
            await _channel.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await Task.WhenAll(
            _bossGroup.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)),
            _workerGroup.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
    }
}
