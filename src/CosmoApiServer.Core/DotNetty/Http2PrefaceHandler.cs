using CosmoApiServer.Core.Middleware;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;

namespace CosmoApiServer.Core.DotNetty;

/// <summary>
/// Byte-sniffing handler that detects the HTTP/2 cleartext connection preface
/// ("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n") and routes the connection to the
/// appropriate codec:
/// <list type="bullet">
///   <item><description>h2c preface detected → <see cref="Http2FrameHandler"/></description></item>
///   <item><description>Anything else → standard HTTP/1.1 decoder stack</description></item>
/// </list>
/// This handler removes itself from the pipeline after making the decision.
/// </summary>
internal sealed class Http2PrefaceHandler : ByteToMessageDecoder
{
    // The h2c connection preface is 24 bytes.
    private static readonly byte[] H2cPreface =
        "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

    private readonly RequestDelegate _appPipeline;
    private readonly IServiceProvider _services;
    private readonly int _maxRequestBodySize;

    public Http2PrefaceHandler(
        RequestDelegate appPipeline,
        IServiceProvider services,
        int maxRequestBodySize)
    {
        _appPipeline = appPipeline;
        _services = services;
        _maxRequestBodySize = maxRequestBodySize;
    }

    protected override void Decode(IChannelHandlerContext ctx, IByteBuffer input, List<object> output)
    {
        if (input.ReadableBytes < H2cPreface.Length)
            return; // wait for more bytes

        bool isH2c = true;
        for (int i = 0; i < H2cPreface.Length; i++)
        {
            if (input.GetByte(input.ReaderIndex + i) != H2cPreface[i])
            {
                isH2c = false;
                break;
            }
        }

        var pipeline = ctx.Channel.Pipeline;

        if (isH2c)
        {
            pipeline.AddLast("h2-handler", new Http2FrameHandler(_appPipeline, _services));
        }
        else
        {
            HttpServerChannel.AddHttp11Handlers(pipeline, _appPipeline, _services, _maxRequestBodySize);
        }

        // Remove this one-shot handler; the remaining bytes will be forwarded
        // automatically by ByteToMessageDecoder to the next handler.
        pipeline.Remove(this);
    }
}
