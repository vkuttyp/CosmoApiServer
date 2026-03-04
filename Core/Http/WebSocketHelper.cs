using System.Security.Cryptography;
using System.Text;

namespace CosmoApiServer.Core.Http;

public static class WebSocketHelper
{
    private const string MagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public static bool IsWebSocketRequest(HttpRequest request)
    {
        return request.Headers.TryGetValue("Connection", out var conn) && 
               conn.Contains("Upgrade", StringComparison.OrdinalIgnoreCase) &&
               request.Headers.TryGetValue("Upgrade", out var upgrade) && 
               upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase);
    }

    public static string CreateResponseKey(string requestKey)
    {
        var combined = requestKey + MagicGuid;
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToBase64String(bytes);
    }
}
