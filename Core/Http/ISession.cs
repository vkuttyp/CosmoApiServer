namespace CosmoApiServer.Core.Http;

/// <summary>
/// Per-request session backed by an in-memory store keyed by a cookie session ID.
/// </summary>
public interface ISession
{
    string Id { get; }
    bool IsAvailable { get; }
    IEnumerable<string> Keys { get; }

    void Set(string key, byte[] value);
    bool TryGetValue(string key, out byte[] value);
    void Remove(string key);
    void Clear();
    Task LoadAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
}

public static class SessionExtensions
{
    public static void SetString(this ISession session, string key, string value)
        => session.Set(key, System.Text.Encoding.UTF8.GetBytes(value));

    public static string? GetString(this ISession session, string key)
        => session.TryGetValue(key, out var bytes) ? System.Text.Encoding.UTF8.GetString(bytes) : null;

    public static void SetInt32(this ISession session, string key, int value)
        => session.Set(key, BitConverter.GetBytes(value));

    public static int? GetInt32(this ISession session, string key)
        => session.TryGetValue(key, out var bytes) && bytes.Length == 4 ? BitConverter.ToInt32(bytes) : null;
}
