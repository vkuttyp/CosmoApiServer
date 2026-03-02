using CosmoApiServer.Core.Middleware;
using DotNetty.Codecs.Http;
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
        CancellationToken cancellationToken = default)
    {
        var bootstrap = new ServerBootstrap()
            .Group(_bossGroup, _workerGroup)
            .Channel<TcpServerSocketChannel>()
            .Option(ChannelOption.SoBacklog, 128)
            .ChildOption(ChannelOption.TcpNodelay, true)
            .ChildHandler(new ActionChannelInitializer<ISocketChannel>(channel =>
            {
                var pipeline2 = channel.Pipeline;
                pipeline2.AddLast(new HttpRequestDecoder());
                pipeline2.AddLast(new HttpResponseEncoder());
                pipeline2.AddLast(new HttpObjectAggregator(65536));
                pipeline2.AddLast(new HttpChannelHandler(pipeline, services));
            }));

        _channel = await bootstrap.BindAsync(port);
        Console.WriteLine($"CosmoApiServer listening on http://0.0.0.0:{port}");
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
