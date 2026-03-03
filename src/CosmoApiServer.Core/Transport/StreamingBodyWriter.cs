using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CosmoApiServer.Core.Transport;

/// <summary>
/// Transport-agnostic replacement for DotNetty's ChunkedResponseHelper.
/// Produces a <see cref="Func{Stream,Task}"/> that writes NDJSON chunks from an
/// <see cref="IAsyncEnumerable{T}"/> action result directly to the raw <see cref="Stream"/>
/// provided by the transport layer (HTTP/1.1 ChunkedBodyStream, HTTP/2 data stream, etc.).
/// </summary>
internal static class StreamingBodyWriter
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly MethodInfo CreateDef =
        typeof(StreamingBodyWriter).GetMethod(nameof(CreateTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, MethodInfo> Cache = new();

    /// <summary>
    /// Returns a streaming writer if <paramref name="result"/> is an
    /// <see cref="IAsyncEnumerable{T}"/>, otherwise null.
    /// </summary>
    public static Func<Stream, Task>? TryCreate(object? result, int statusCode)
    {
        if (result is null) return null;
        if (!TryGetElementType(result.GetType(), out var elemType)) return null;
        var factory = Cache.GetOrAdd(elemType!, t => CreateDef.MakeGenericMethod(t));
        return (Func<Stream, Task>)factory.Invoke(null, [result, statusCode])!;
    }

    // Called via reflection — one compiled delegate per element type
    private static Func<Stream, Task> CreateTyped<T>(IAsyncEnumerable<T> source, int _statusCode)
    {
        return async bodyStream =>
        {
            await using var enumerator = source.GetAsyncEnumerator();

            bool hasItem;
            T? firstItem = default;
            try
            {
                hasItem = await enumerator.MoveNextAsync();
                if (hasItem) firstItem = enumerator.Current;
            }
            catch (Exception ex)
            {
                // Enumerator failed before we could write — best effort: write an error line
                var errLine = JsonSerializer.Serialize(new { error = ex.Message }, CamelCase) + "\n";
                await bodyStream.WriteAsync(Encoding.UTF8.GetBytes(errLine));
                return;
            }

            if (!hasItem) return; // empty — transport will terminate with 0-chunk

            // First item
            await WriteLineAsync(bodyStream, firstItem);

            // Remaining items
            try
            {
                while (await enumerator.MoveNextAsync())
                    await WriteLineAsync(bodyStream, enumerator.Current);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Stream ERROR] {ex.GetType().Name}: {ex.Message}");
            }
        };
    }

    private static async Task WriteLineAsync<T>(Stream stream, T item)
    {
        var json = JsonSerializer.Serialize(item, CamelCase) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes);
    }

    private static bool TryGetElementType(Type type, out Type? elementType)
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
}
