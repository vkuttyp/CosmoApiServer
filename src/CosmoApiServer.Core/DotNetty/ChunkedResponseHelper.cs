using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Channels;

namespace CosmoApiServer.Core.DotNetty;

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

            // ── Peek first item before committing to any response ────────────
            bool hasItem;
            T? firstItem = default;
            try
            {
                Console.WriteLine("[Stream] Fetching first item...");
                hasItem = await enumerator.MoveNextAsync();
                if (hasItem) firstItem = enumerator.Current;
                Console.WriteLine($"[Stream] hasItem={hasItem}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Stream ERROR] {ex.GetType().Name}: {ex.Message}");
                await enumerator.DisposeAsync();
                await SendErrorAsync(ctx, 500, ex.Message);
                return;
            }

            // ── Send headers (Connection: close so client knows when done) ───
            var httpResponse = new DefaultHttpResponse(
                HttpVersion.Http11,
                HttpResponseStatus.ValueOf(statusCode));
            httpResponse.Headers.Set(HttpHeaderNames.TransferEncoding, HttpHeaderValues.Chunked);
            httpResponse.Headers.Set(HttpHeaderNames.ContentType, "application/x-ndjson");
            httpResponse.Headers.Set(HttpHeaderNames.Connection, "close");  // client closes when 0-chunk received

            Console.WriteLine("[Stream] Sending headers...");
            await ctx.WriteAndFlushAsync(httpResponse);
            Console.WriteLine("[Stream] Headers sent");

            // ── Empty result ─────────────────────────────────────────────────
            if (!hasItem)
            {
                await ctx.WriteAndFlushAsync(EmptyLastHttpContent.Default);
                await ctx.CloseAsync();
                await enumerator.DisposeAsync();
                Console.WriteLine("[Stream] Done (empty)");
                return;
            }

            // ── First item ───────────────────────────────────────────────────
            var firstJson = JsonSerializer.Serialize(firstItem!, CamelCase) + "\n";
            Console.WriteLine($"[Stream] Sending first item ({firstJson.Length} chars)...");
            await ctx.WriteAndFlushAsync(new DefaultHttpContent(
                Unpooled.CopiedBuffer(firstJson, Encoding.UTF8)));
            Console.WriteLine("[Stream] First item sent");

            // ── Remaining items ──────────────────────────────────────────────
            int count = 1;
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    count++;
                    var json = JsonSerializer.Serialize(enumerator.Current, CamelCase) + "\n";
                    await ctx.WriteAndFlushAsync(new DefaultHttpContent(
                        Unpooled.CopiedBuffer(json, Encoding.UTF8)));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Stream ERROR mid-stream] {ex.GetType().Name}: {ex.Message}");
                await ctx.CloseAsync();
                return;
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            // ── Signal end of chunked response ───────────────────────────────
            await ctx.WriteAndFlushAsync(EmptyLastHttpContent.Default);
            await ctx.CloseAsync();
            Console.WriteLine($"[Stream] Done ({count} items)");
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
