using System.Collections.Concurrent;
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

    private static readonly MethodInfo CreateTypedWriterDef =
        typeof(ChunkedResponseHelper).GetMethod(nameof(CreateTypedWriter), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly ConcurrentDictionary<Type, MethodInfo> MethodCache = new();

    public static Func<object, Task>? TryCreateStreamWriter(object? result, int statusCode)
    {
        if (result is null) return null;
        if (!TryGetAsyncEnumerableElementType(result.GetType(), out var elemType)) return null;
        var factory = MethodCache.GetOrAdd(elemType!, t => CreateTypedWriterDef.MakeGenericMethod(t));
        return (Func<object, Task>)factory.Invoke(null, [result, statusCode])!;
    }

    private static bool TryGetAsyncEnumerableElementType(Type type, out Type? elementType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }
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
            var enumerator = source.GetAsyncEnumerator();

            // Peek at the first item BEFORE sending headers.
            // If the DB throws here we can still return a proper JSON error response.
            bool hasItem;
            T? firstItem = default;
            try
            {
                hasItem = await enumerator.MoveNextAsync();
                if (hasItem) firstItem = enumerator.Current;
            }
            catch (Exception ex)
            {
                await enumerator.DisposeAsync();
                await SendErrorAsync(ctx, 500, ex.Message);
                return;
            }

            // Commit to chunked streaming — headers sent here, status code is now locked in
            var httpResponse = new DefaultHttpResponse(
                HttpVersion.Http11,
                HttpResponseStatus.ValueOf(statusCode));
            httpResponse.Headers.Set(HttpHeaderNames.TransferEncoding, HttpHeaderValues.Chunked);
            httpResponse.Headers.Set(HttpHeaderNames.ContentType, "application/json");
            httpResponse.Headers.Set(HttpHeaderNames.Connection, HttpHeaderValues.KeepAlive);
            await ctx.WriteAndFlushAsync(httpResponse);

            // Opening bracket + first item (if any)
            if (!hasItem)
            {
                await ctx.WriteAsync(new DefaultHttpContent(Unpooled.CopiedBuffer("[]", Encoding.UTF8)));
                await ctx.WriteAndFlushAsync(EmptyLastHttpContent.Default);
                await enumerator.DisposeAsync();
                return;
            }

            await ctx.WriteAndFlushAsync(new DefaultHttpContent(
                Unpooled.CopiedBuffer("[" + JsonSerializer.Serialize(firstItem!, CamelCase), Encoding.UTF8)));

            // Stream remaining items
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    var json = "," + JsonSerializer.Serialize(enumerator.Current, CamelCase);
                    await ctx.WriteAndFlushAsync(new DefaultHttpContent(
                        Unpooled.CopiedBuffer(json, Encoding.UTF8)));
                }
            }
            catch
            {
                // Mid-stream: headers already sent, can't change status — close connection
                await ctx.CloseAsync();
                return;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            // Close JSON array and end chunked transfer
            await ctx.WriteAsync(new DefaultHttpContent(Unpooled.CopiedBuffer("]", Encoding.UTF8)));
            await ctx.WriteAndFlushAsync(EmptyLastHttpContent.Default);
        };
    }

    private static async Task SendErrorAsync(IChannelHandlerContext ctx, int status, string message)
    {
        var json = JsonSerializer.Serialize(new { error = message }, CamelCase);
        var body = Unpooled.CopiedBuffer(json, Encoding.UTF8);
        var response = new DefaultFullHttpResponse(
            HttpVersion.Http11,
            HttpResponseStatus.ValueOf(status),
            body);
        response.Headers.Set(HttpHeaderNames.ContentType, "application/json");
        response.Headers.Set(HttpHeaderNames.ContentLength, body.ReadableBytes);
        await ctx.WriteAndFlushAsync(response);
    }
}
