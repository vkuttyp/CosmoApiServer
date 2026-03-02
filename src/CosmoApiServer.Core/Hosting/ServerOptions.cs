namespace CosmoApiServer.Core.Hosting;

public sealed class ServerOptions
{
    public int Port { get; set; } = 5000;
    public int MaxRequestBodySize { get; set; } = 65536; // 64 KB
}
