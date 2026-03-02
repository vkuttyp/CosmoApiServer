using System.Reflection;
using System.Text;
using System.Text.Json;
using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Channels;

namespace CosmoApiServer.Core.DotNetty;

/// <summary>
/// Detects IAsyncEnumerable&lt;T&gt; return values and builds a DotNetty chunked-transfer writer.
/// Keeps DotNetty details out of ControllerScanner.
/// </summary>
internal static class ChunkedResponseHelper
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// If <paramref name="result"/> implements IAsyncEnumerable&lt;T&gt;, returns a writer delegate
    /// that streams it as a JSON array using HTTP chunked transfer encoding.
    /// The delegate receives an <see cref="IChannelHandlerContext"/> boxed as <see cref="object"/>.
    /// </summary>
    public static Func<object, Task>? TryCreateStreamWriter(object? result, int statusCode)
    {
        if (result is null) return null;

        if (!TryGetAsyncEnumerableElementType(result.GetType(), out var elemType))
            return null;

        var factory = typeof(ChunkedResponseHelper)
            .GetMethod(nameof(CreateTypedWriter), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(elemType!);

        return (Func<object, Task>)factory.Invoke(null, [result, statusCode])!;
    }

    private static bool TryGetAsyncEnumerableElementType(Type type, out Type? elementType)
    {
        // Direct: IAsyncEnumerable<T>
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        // Via interface (e.g. compiler-generated async iterator state machine)
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
            {
                elementType = iface.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null;
        return false;
    }

    private static Func<object, Task> CreateTypedWriter<T>(IAsyncEnumerable<T> source, int statusCode)
    {
        return async nettyCtxObj =>
        {
            var ctx = (IChannelHandlerContext)nettyCtxObj;

            // Write response headers — no Content-Length, chunked transfer
            var httpResponse = new DefaultHttpResponse(
                HttpVersion.Http11,
                HttpResponseStatus.ValueOf(statusCode));
            httpResponse.Headers.Set(HttpHeaderNames.TransferEncoding, HttpHeaderValues.Chunked);
            httpResponse.Headers.Set(HttpHeaderNames.ContentType, "application/json");
            httpResponse.Headers.Set(HttpHeaderNames.Connection, HttpHeaderValues.KeepAlive);
            await ctx.WriteAsync(httpResponse);

            // Opening bracket
            await ctx.WriteAsync(new DefaultHttpContent(
                Unpooled.CopiedBuffer("[", Encoding.UTF8)));

            bool first = true;
            try
            {
                await foreach (var item in source)
                {
                    var json = (first ? "" : ",") + JsonSerializer.Serialize(item, CamelCase);
                    first = false;
                    await ctx.WriteAsync(new DefaultHttpContent(
                        Unpooled.CopiedBuffer(json, Encoding.UTF8)));
                }
            }
            catch
            {
                // Headers already sent; close cleanly
                await ctx.CloseAsync();
                return;
            }

            // Closing bracket + end-of-chunks marker
            await ctx.WriteAsync(new DefaultHttpContent(
                Unpooled.CopiedBuffer("]", Encoding.UTF8)));
            await ctx.WriteAndFlushAsync(EmptyLastHttpContent.Default);
        };
    }
}
